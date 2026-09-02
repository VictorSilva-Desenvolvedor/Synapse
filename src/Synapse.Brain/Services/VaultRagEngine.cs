using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Search;

namespace Synapse.Brain.Services;

/// <summary>
/// Motor de Busca Semântica Vetorial e RAG (Retrieval-Augmented Generation) para o cofre do Obsidian (V5.1).
/// </summary>
public sealed class VaultRagEngine : IVaultBrainQuery
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IBrainAiProvider _aiProvider;
    private readonly BrainConfig _config;
    private readonly IVaultIndexStore _indexStore;
    // ConcurrentDictionary, nao Dictionary: o Tray indexa o cofre em background enquanto o usuario
    // pergunta no chat, e as duas coisas caem neste mesmo campo. A versao anterior protegia so as DUAS
    // escritas com lock (_index) e deixava os outros dez acessos livres - inclusive o foreach da busca.
    // Percorrer um Dictionary enquanto outra thread insere chave nova lanca "Collection was modified",
    // reproduzido em teste. Nota: reescrever o valor de uma chave existente NAO dispara isso, so mudanca
    // estrutural, e foi por isso que o defeito sobreviveu tanto tempo - reindexar o mesmo conjunto de
    // notas nunca quebrava; bastava uma nota nova durante uma pergunta.
    private readonly ConcurrentDictionary<string, NoteEmbeddingEntry> _index = new(StringComparer.OrdinalIgnoreCase);

    public IHybridSearchService? HybridSearchService { get; set; }

    public VaultRagEngine(
        IEmbeddingProvider embeddingProvider,
        IBrainAiProvider aiProvider,
        BrainConfig? config = null,
        IVaultIndexStore? indexStore = null,
        IHybridSearchService? hybridSearchService = null)
    {
        _embeddingProvider = embeddingProvider;
        _aiProvider = aiProvider;
        _config = config ?? new BrainConfig();
        _indexStore = indexStore ?? new FileVaultIndexStore();
        HybridSearchService = hybridSearchService;
    }

    public async Task IndexVaultAsync(string vaultRootPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath) || !Directory.Exists(vaultRootPath))
        {
            return;
        }

        if (_index.Count == 0)
        {
            var loaded = await _indexStore.LoadAsync(vaultRootPath, ct);
            if (loaded != null)
            {
                foreach (var (k, v) in loaded)
                {
                    _index[k] = v;
                }
            }
        }

        // VaultIndexFilter e a MESMA regra usada pelo indice de busca (bulk e watcher). Antes daqui o
        // filtro era uma lista propria, que nao excluia os registros de atividade que o Synapse grava
        // no cofre - entao mesmo com a busca por palavra ja limpa, o caminho semantico continuava
        // trazendo os logs de volta como "notas consultadas".
        var files = Directory.GetFiles(vaultRootPath, "*.md", SearchOption.AllDirectories)
            .Where(f => !VaultIndexFilter.ShouldIgnore(HybridSearchEngine.ToCanonicalRelativePath(f, vaultRootPath)))
            .ToList();

        var currentRelativePaths = new HashSet<string>(
            files.Select(f => HybridSearchEngine.ToCanonicalRelativePath(f, vaultRootPath)),
            StringComparer.OrdinalIgnoreCase);

        var removedAny = false;
        var staleKeys = _index.Keys.Where(k => !currentRelativePaths.Contains(k)).ToList();
        foreach (var staleKey in staleKeys)
        {
            _index.TryRemove(staleKey, out _);
            removedAny = true;
        }

        var toIndex = new List<(string FilePath, string RelativePath, string Text, string Hash)>();
        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            var relativePath = HybridSearchEngine.ToCanonicalRelativePath(file, vaultRootPath);
            string text;
            try
            {
                text = await File.ReadAllTextAsync(file, ct);
            }
            catch
            {
                continue;
            }

            var hash = ComputeSha256(text);
            if (_index.TryGetValue(relativePath, out var existing) && existing.ContentHash == hash)
            {
                continue; // Já indexado e inalterado
            }

            toIndex.Add((file, relativePath, text, hash));
        }

        if (toIndex.Count > 0)
        {
            using var semaphore = new SemaphoreSlim(4);
            var tasks = toIndex.Select(async item =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var vector = await _embeddingProvider.GenerateEmbeddingAsync(item.Text, ct);
                    var title = Path.GetFileNameWithoutExtension(item.RelativePath);
                    var tokens = Tokenize(title, item.Text);
                    var tokenSet = new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);
                    var titleTokenSet = new HashSet<string>(Tokenize(title), StringComparer.OrdinalIgnoreCase);
                    // Sem lock: a atribuicao pelo indexador do ConcurrentDictionary ja e atomica.
                    _index[item.RelativePath] = new NoteEmbeddingEntry(item.RelativePath, item.Hash, vector, DateTimeOffset.UtcNow, tokens, tokenSet, titleTokenSet);
                }
                catch
                {
                    // Ignora falha em arquivo individual bloqueado
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        if (toIndex.Count > 0 || removedAny)
        {
            await _indexStore.SaveAsync(vaultRootPath, _index, ct);
        }
    }

    public async Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        string query,
        string vaultRootPath,
        int topK = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        // 1. Prioridade para busca por software (FTS5 em ~2ms)
        var lexicalMatches = new List<HybridSearchResult>();
        if (HybridSearchService != null)
        {
            try
            {
                await foreach (var match in HybridSearchService.SearchAsync(query, isRegex: false, limit: Math.Max(topK, 10), ct: ct).ConfigureAwait(false))
                {
                    var fullPath = Path.Combine(vaultRootPath, match.FilePath);
                    if (File.Exists(fullPath))
                    {
                        lexicalMatches.Add(match);
                    }
                }
            }
            catch
            {
                lexicalMatches.Clear();
            }
        }

        // 2. Qualquer resultado do FTS5 ja e resposta: retorna direto, SEM chamar o IEmbeddingProvider.
        // O limiar anterior era >= 3, e isso tratava uma busca PRECISA como fracasso: "qual minha
        // lista de amigos?" encontrava exatamente as 2 notas certas, caia no caminho semantico por
        // ter menos de 3, pagava o carregamento do modelo de embedding (2,3s a 9,5s) e ainda trazia
        // de volta os registros de atividade pelo indice do RAG. Encontrar pouco e o resultado
        // desejado quando o cofre tem pouco sobre o assunto; so o vazio significa que a busca por
        // palavra falhou e a rede de seguranca semantica precisa entrar.
        const int SufficientLexicalThreshold = 1;
        if (lexicalMatches.Count >= SufficientLexicalThreshold)
        {
            var topMatches = lexicalMatches.Take(topK).ToList();
            var results = new List<SemanticSearchResult>(topMatches.Count);

            foreach (var match in topMatches)
            {
                var title = Path.GetFileNameWithoutExtension(match.FilePath);
                var excerpt = ReadExcerpt(vaultRootPath, match.FilePath, query, match.Snippet, match.RipgrepMatches);
                results.Add(new SemanticSearchResult(match.FilePath, title, excerpt, (float)match.Score));
            }

            return EnforceGlobalContextBudget(results, maxChars: 16000);
        }

        // 3. Caso contrário (< 3 resultados, ex: pergunta puramente conceitual):
        // Rede de segurança semântica entra em ação com IEmbeddingProvider
        if (_index.Count == 0)
        {
            await IndexVaultAsync(vaultRootPath, ct);
        }

        if (_index.Count == 0) return [];

        var queryVector = await _embeddingProvider.GenerateEmbeddingAsync(query, ct);
        var queryTokens = Tokenize(query);

        var entriesWithSim = new List<(string RelativePath, string Title, NoteEmbeddingEntry Entry, float SemanticSimilarity)>(_index.Count);
        foreach (var (relativePath, entry) in _index)
        {
            var semanticSim = VectorMath.CosineSimilarity(queryVector, entry.Vector);
            var title = Path.GetFileNameWithoutExtension(relativePath);
            entriesWithSim.Add((relativePath, title, entry, semanticSim));
        }

        // Ranking Semântico (1-indexed) sobre todas as notas
        var semanticRanking = entriesWithSim
            .OrderByDescending(e => e.SemanticSimilarity)
            .Select((item, idx) => (item.RelativePath, Rank: idx + 1))
            .ToDictionary(x => x.RelativePath, x => x.Rank, StringComparer.OrdinalIgnoreCase);

        // Ranking Léxico
        Dictionary<string, int> lexicalRanking;
        if (lexicalMatches.Count > 0)
        {
            lexicalRanking = lexicalMatches
                .Select((m, idx) => (m.FilePath, Rank: idx + 1))
                .ToDictionary(x => x.FilePath, x => x.Rank, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            lexicalRanking = ComputeLegacyLexicalRanking(queryTokens, entriesWithSim);
        }

        // Reciprocal Rank Fusion (RRF): score = 1/(60 + rank_sem) + (has_lex ? 1/(60 + rank_lex) : 0)
        const float k = 60f;
        var fusedResults = new List<SemanticSearchResult>(entriesWithSim.Count);
        var matchLookup = lexicalMatches.ToDictionary(m => m.FilePath, StringComparer.OrdinalIgnoreCase);

        foreach (var item in entriesWithSim)
        {
            var semRank = semanticRanking[item.RelativePath];
            var semScore = 1f / (k + semRank);

            var lexScore = lexicalRanking.TryGetValue(item.RelativePath, out var lexRank)
                ? 1f / (k + lexRank)
                : 0f;

            var finalScore = semScore + lexScore;
            fusedResults.Add(new SemanticSearchResult(item.RelativePath, item.Title, "", finalScore));
        }

        var topResults = fusedResults
            .OrderByDescending(r => r.SimilarityScore)
            .Take(topK)
            .ToList();

        for (int i = 0; i < topResults.Count; i++)
        {
            var result = topResults[i];
            matchLookup.TryGetValue(result.RelativePath, out var m);
            topResults[i] = result with
            {
                Excerpt = ReadExcerpt(vaultRootPath, result.RelativePath, query, m?.Snippet, m?.RipgrepMatches)
            };
        }

        return EnforceGlobalContextBudget(topResults, maxChars: 16000);
    }

    /// <summary>Contador de leituras de excerto em disco, para os testes provarem que so os topK sao lidos.</summary>
    internal static int ExcerptReadCount;

    /// <summary>
    /// Extrai o excerto adequado para o envio à IA:
    /// - Nota pequena (&lt;= 4.000 chars): enviada por completo.
    /// - Nota grande (&gt; 4.000 chars): enviada a passagem do casamento (+-10 linhas ao redor do match)
    ///   para preservar tabelas e blocos inteiros; início do arquivo apenas como último recurso.
    /// - Tags HTML são removidas.
    /// </summary>
    internal static string ReadExcerpt(
        string vaultRootPath,
        string relativePath,
        string query = "",
        string? snippet = null,
        IReadOnlyList<RipgrepMatch>? ripgrepMatches = null)
    {
        var fullPath = Path.Combine(vaultRootPath, relativePath);
        if (!File.Exists(fullPath))
        {
            return "";
        }

        Interlocked.Increment(ref ExcerptReadCount);

        try
        {
            var content = File.ReadAllText(fullPath);

            // 1. Nota pequena (<= 4.000 chars) vai inteira
            if (content.Length <= 4000)
            {
                return StripHtml(content);
            }

            // 2. Nota grande (> 4.000 chars): extrai +-10 linhas ao redor do match
            var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            int matchLineIndex = -1;

            // Prioridade A: linha exata do match vinda do Ripgrep
            if (ripgrepMatches != null && ripgrepMatches.Count > 0)
            {
                matchLineIndex = ripgrepMatches[0].LineNumber - 1;
            }

            // Prioridade B: localiza nas linhas onde a query ou termos aparecem
            if (matchLineIndex < 0 && !string.IsNullOrWhiteSpace(query))
            {
                var queryTrimmed = query.Trim();
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(queryTrimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        matchLineIndex = i;
                        break;
                    }
                }

                if (matchLineIndex < 0)
                {
                    var tokens = Tokenize(query);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var lineTokens = Tokenize(lines[i]);
                        if (tokens.Any(t => lineTokens.Contains(t)))
                        {
                            matchLineIndex = i;
                            break;
                        }
                    }
                }
            }

            // Prioridade C: localiza pelo texto do snippet FTS5
            if (matchLineIndex < 0 && !string.IsNullOrWhiteSpace(snippet))
            {
                var cleanSnippet = StripHtml(snippet).Trim();
                var snippetTerms = Tokenize(cleanSnippet);
                if (snippetTerms.Count > 0)
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var lineTokens = Tokenize(lines[i]);
                        if (snippetTerms.Any(t => lineTokens.Contains(t)))
                        {
                            matchLineIndex = i;
                            break;
                        }
                    }
                }
            }

            // Se localizou a linha do match, extrai +- 10 linhas ao redor
            if (matchLineIndex >= 0 && matchLineIndex < lines.Length)
            {
                int start = Math.Max(0, matchLineIndex - 10);
                int count = Math.Min(lines.Length - start, (matchLineIndex + 10) - start + 1);
                var passage = string.Join(Environment.NewLine, lines.Skip(start).Take(count));
                return StripHtml(passage);
            }

            // Se não localizou a linha mas tem snippet FTS5 limpo, usa o snippet
            if (!string.IsNullOrWhiteSpace(snippet))
            {
                return StripHtml(snippet);
            }

            // Último recurso: primeiras 20 linhas da nota
            return StripHtml(string.Join(Environment.NewLine, lines.Take(20)));
        }
        catch
        {
            return "";
        }
    }

    internal static string StripHtml(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        return Regex.Replace(input, @"<[^>]+>", "");
    }

    /// <summary>
    /// Enforça o teto global de caracteres (~16.000) no contexto conjunto de notas,
    /// cortando as notas menos relevantes primeiro e nunca cortando no meio de uma linha de tabela.
    /// </summary>
    internal static List<SemanticSearchResult> EnforceGlobalContextBudget(
        IReadOnlyList<SemanticSearchResult> results,
        int maxChars = 16000)
    {
        var output = new List<SemanticSearchResult>(results.Count);
        int remainingBudget = maxChars;

        foreach (var result in results)
        {
            if (remainingBudget <= 0)
            {
                break;
            }

            var excerpt = result.Excerpt;
            if (string.IsNullOrEmpty(excerpt))
            {
                output.Add(result);
                continue;
            }

            if (excerpt.Length <= remainingBudget)
            {
                output.Add(result);
                remainingBudget -= excerpt.Length;
            }
            else
            {
                var trimmed = TrimExcerptToBudget(excerpt, remainingBudget);
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    output.Add(result with { Excerpt = trimmed });
                    remainingBudget -= trimmed.Length;
                }
                break;
            }
        }

        return output;
    }

    private static string TrimExcerptToBudget(string excerpt, int budget)
    {
        if (budget <= 0) return "";
        var lines = excerpt.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            int cost = (sb.Length > 0 ? Environment.NewLine.Length : 0) + line.Length;
            if (sb.Length + cost > budget)
            {
                // Para antes da linha que ultrapassaria o orçamento.
                // Isso garante que nenhuma linha (especialmente de tabela | ... |) é cortada no meio.
                break;
            }

            if (sb.Length > 0)
            {
                sb.AppendLine();
            }
            sb.Append(line);
        }

        return sb.ToString();
    }

    private static Dictionary<string, int> ComputeLegacyLexicalRanking(
        List<string> queryTokens,
        List<(string RelativePath, string Title, NoteEmbeddingEntry Entry, float SemanticSimilarity)> entries)
    {
        if (queryTokens.Count == 0) return new(StringComparer.OrdinalIgnoreCase);

        var scores = new List<(string RelativePath, float LexicalScore)>();
        foreach (var item in entries)
        {
            float matchedWeight = 0f;
            foreach (var qt in queryTokens)
            {
                if (item.Entry.TitleTokenSet.Contains(qt))
                {
                    matchedWeight += 3.0f; // Peso maior para match no título
                }
                else if (item.Entry.TokenSet.Contains(qt))
                {
                    matchedWeight += 1.0f;
                }
            }

            var score = matchedWeight / (queryTokens.Count * 3.0f);
            if (score > 0)
            {
                scores.Add((item.RelativePath, score));
            }
        }

        return scores
            .OrderByDescending(s => s.LexicalScore)
            .Select((s, idx) => (s.RelativePath, Rank: idx + 1))
            .ToDictionary(x => x.RelativePath, x => x.Rank, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<RagAnswer> AskVaultAsync(
        string question,
        string vaultRootPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var topNotes = await SearchAsync(question, vaultRootPath, topK: 4, ct);
        if (topNotes.Count == 0)
        {
            return new RagAnswer(question, "Não encontrei notas relevantes no seu cofre para responder a essa pergunta.", []);
        }

        var contextBuilder = new StringBuilder();
        foreach (var note in topNotes)
        {
            var excerpt = !string.IsNullOrWhiteSpace(note.Excerpt)
                ? note.Excerpt
                : ReadExcerpt(vaultRootPath, note.RelativePath, question);

            if (!string.IsNullOrWhiteSpace(excerpt))
            {
                contextBuilder.AppendLine($"--- INÍCIO DA NOTA: [[{note.Title}]] ---");
                contextBuilder.AppendLine(excerpt);
                contextBuilder.AppendLine($"--- FIM DA NOTA ---");
                contextBuilder.AppendLine();
            }
        }

        var prompt = $@"Você é o assistente inteligente de Segundo Cérebro do usuário no Obsidian.
Com base no contexto das notas do cofre fornecidas abaixo, responda à pergunta de forma direta, clara e bem estruturada em Markdown, citando as notas relevantes com wikilinks [[Nome da Nota]].
NUNCA repita este prompt, blocos brutos de notas ou o cabeçalho da pergunta. Responda diretamente ao usuário como um assistente prestativo.

Notas do cofre relevantes:
{contextBuilder}

Pergunta do usuário:
""{question}""";

        var rawAnswer = await _aiProvider.AskQuestionAsync(prompt, ct);
        var finalAnswer = NoteFileWriter.SanitizeBodyMarkdown(rawAnswer);
        try
        {
            var refined = await _aiProvider.RefineAnswerAsync(question, rawAnswer, ct);
            if (!string.IsNullOrWhiteSpace(refined))
            {
                finalAnswer = refined;
            }
        }
        catch
        {
            // Mantém rawAnswer sanitizado
        }

        return new RagAnswer(question, finalAnswer, topNotes);
    }

    public async Task<string> SaveAnswerAsNoteAsync(
        RagAnswer answer,
        string vaultRootPath,
        string targetSubFolder = "Brain/Conversas",
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRootPath);

        // 1. Constrói o conteúdo Markdown com frontmatter estruturado
        var fullNoteMarkdown = BuildAnswerNote(answer);

        // 2. Sanitiza e trunca o título a partir da pergunta (máximo ~80 caracteres)
        var sanitizedTitle = SanitizeQuestionFileName(answer.Question);

        var targetDir = Path.Combine(vaultRootPath, targetSubFolder);
        Directory.CreateDirectory(targetDir);

        var targetFilePath = Path.Combine(targetDir, $"{sanitizedTitle}.md");

        // 3. Se o arquivo já existir, anexa sufixo numérico
        var count = 1;
        while (File.Exists(targetFilePath))
        {
            targetFilePath = Path.Combine(targetDir, $"{sanitizedTitle} ({count++}).md");
        }

        // 4. Grava a nota no disco do cofre
        await File.WriteAllTextAsync(targetFilePath, fullNoteMarkdown, Encoding.UTF8, ct);

        return Path.GetRelativePath(vaultRootPath, targetFilePath).Replace('\\', '/');
    }

    /// <summary>
    /// Processa uma mensagem do usuário na conversa com o Segundo Cérebro, decidindo
    /// se deve capturar nova nota, responder com base no cofre (RAG), ambos ou responder amigavelmente.
    /// </summary>
    public async Task<ChatTurnOutcome> ProcessChatTurnAsync(
        string userMessage,
        string vaultRootPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRootPath);

        // 1. Busca semântica para notas relacionadas ao conteúdo da mensagem
        var relatedNotes = await SearchAsync(userMessage, vaultRootPath, topK: 4, ct);

        // 2. Levanta pastas de categorias existentes e títulos de notas para contextualização da IA
        var existingCategoryFolders = NoteFileWriter.GetExistingCategoryFolders(vaultRootPath, _config.DefaultFolder);
        var existingVaultNotes = NoteFileWriter.GetVaultNoteTitles(vaultRootPath);

        // 3. Processa a intenção com o provedor de IA
        var turnResult = await _aiProvider.ProcessChatTurnAsync(
            userMessage,
            existingVaultNotes,
            existingCategoryFolders,
            relatedNotes,
            ct);

        // 3.1. Segundo passo: refinamento da resposta do chat pela IA para entregar apenas o essencial
        if (!string.IsNullOrWhiteSpace(turnResult.ReplyMessage))
        {
            try
            {
                var refined = await _aiProvider.RefineAnswerAsync(
                    userMessage,
                    turnResult.ReplyMessage,
                    ct);

                if (!string.IsNullOrWhiteSpace(refined))
                {
                    turnResult.ReplyMessage = refined;
                }
            }
            catch
            {
                turnResult.ReplyMessage = NoteFileWriter.SanitizeBodyMarkdown(turnResult.ReplyMessage);
            }
        }

        string? savedNotePath = null;

        // 4. Se ShouldCapture=true, formata e grava a nova nota estruturada
        if (turnResult.ShouldCapture)
        {
            var structured = new AiStructuredNote
            {
                Title = string.IsNullOrWhiteSpace(turnResult.Title) ? "Nova Anotacao" : turnResult.Title,
                Category = string.IsNullOrWhiteSpace(turnResult.Category) ? "Conceito" : turnResult.Category,
                Tags = turnResult.Tags ?? [],
                Summary = turnResult.KeyPoints.Count > 0 ? string.Join("; ", turnResult.KeyPoints) : string.Empty,
                KeyPoints = turnResult.KeyPoints ?? [],
                BodyMarkdown = string.IsNullOrWhiteSpace(turnResult.BodyMarkdown) ? userMessage : turnResult.BodyMarkdown,
                SuggestedConnections = turnResult.SuggestedConnections ?? []
            };

            savedNotePath = await NoteFileWriter.WriteStructuredNoteAsync(
                structured,
                vaultRootPath,
                _config,
                existingVaultNotes,
                ct);

            // Atualiza o índice vetorial em memória para a nota nova sem precisar reindexar todo o cofre
            var fullSavedPath = Path.Combine(vaultRootPath, savedNotePath);
            if (File.Exists(fullSavedPath))
            {
                try
                {
                    var noteContent = await File.ReadAllTextAsync(fullSavedPath, ct);
                    var vector = await _embeddingProvider.GenerateEmbeddingAsync(noteContent, ct);
                    var hash = ComputeSha256(noteContent);
                    var title = Path.GetFileNameWithoutExtension(savedNotePath);
                    var tokens = Tokenize(title, noteContent);
                    var tokenSet = new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);
                    var titleTokenSet = new HashSet<string>(Tokenize(title), StringComparer.OrdinalIgnoreCase);
                    _index[savedNotePath] = new NoteEmbeddingEntry(savedNotePath, hash, vector, DateTimeOffset.UtcNow, tokens, tokenSet, titleTokenSet);
                    await _indexStore.SaveAsync(vaultRootPath, _index, ct);
                }
                catch
                {
                    // Falha silenciosa de embedding na nota nova não impede o sucesso do chat
                }
            }
        }

        // 5. Determina fontes: se ShouldAnswer=true, retorna as fontes consultadas; se não, lista vazia
        IReadOnlyList<SemanticSearchResult> sources = turnResult.ShouldAnswer
            ? relatedNotes
            : [];

        return new ChatTurnOutcome(turnResult.ReplyMessage, savedNotePath, sources);
    }

    public static List<string> Tokenize(params string[] inputs)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input)) continue;
            var normalized = RemoveDiacritics(input.ToLowerInvariant());
            var parts = Regex.Split(normalized, @"[^\p{L}\p{Nd}]+");
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    set.Add(trimmed);
                }
            }
        }
        return [.. set];
    }

    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(normalizedString.Length);
        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }
        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string BuildAnswerNote(RagAnswer answer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"titulo: \"{answer.Question.Replace("\"", "\\\"")}\"");
        sb.AppendLine("categoria: \"Chat com o Cofre\"");
        sb.AppendLine($"criado_em: \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"");
        sb.AppendLine("status: processado");
        sb.AppendLine("tags:");
        sb.AppendLine("  - chat-cofre");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {answer.Question}");
        sb.AppendLine();
        var cleanAnswer = NoteFileWriter.SanitizeBodyMarkdown(answer.Answer.Trim());

        sb.AppendLine(cleanAnswer);

        if (answer.Sources.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Fontes Consultadas");
            foreach (var src in answer.Sources)
            {
                if (!string.IsNullOrWhiteSpace(src.Title) && !src.Title.Contains("Você é o assistente"))
                {
                    sb.AppendLine($"- [[{src.Title}]]");
                }
            }
        }

        return sb.ToString();
    }

    private static string SanitizeQuestionFileName(string question)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(question.Where(c => !invalid.Contains(c))).Trim();

        if (sanitized.Length > 80)
        {
            sanitized = sanitized[..80].Trim();
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "Resposta-Chat" : sanitized;
    }

    private static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }
}
