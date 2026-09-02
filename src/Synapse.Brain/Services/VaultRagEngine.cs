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

        var files = Directory.GetFiles(vaultRootPath, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".obsidian") && !f.Contains("_conflitos") && !f.Contains(".trash"))
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

        if (_index.Count == 0)
        {
            await IndexVaultAsync(vaultRootPath, ct);
        }

        if (_index.Count == 0) return [];

        var queryVector = await _embeddingProvider.GenerateEmbeddingAsync(query, ct);
        var queryTokens = Tokenize(query);

        // Rede de segurança: todas as notas seguem recebendo score semântico (recall total)
        var entriesWithSim = new List<(string RelativePath, string Title, NoteEmbeddingEntry Entry, float SemanticSimilarity)>(_index.Count);

        foreach (var (relativePath, entry) in _index)
        {
            var semanticSim = VectorMath.CosineSimilarity(queryVector, entry.Vector);
            var title = Path.GetFileNameWithoutExtension(relativePath);
            entriesWithSim.Add((relativePath, title, entry, semanticSim));
        }

        // 1. Ranking Semântico (1-indexed) sobre todas as notas
        var semanticRanking = entriesWithSim
            .OrderByDescending(e => e.SemanticSimilarity)
            .Select((item, idx) => (item.RelativePath, Rank: idx + 1))
            .ToDictionary(x => x.RelativePath, x => x.Rank, StringComparer.OrdinalIgnoreCase);

        // 2. Ranking Léxico: obtido via IHybridSearchService (top ~200 em milissegundos)
        // com degradação graciosa em caso de serviço nulo ou falha transitória
        Dictionary<string, int>? lexicalRanking = null;
        if (HybridSearchService != null)
        {
            try
            {
                lexicalRanking = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int rank = 1;
                await foreach (var match in HybridSearchService.SearchAsync(query, isRegex: false, limit: 200, ct: ct).ConfigureAwait(false))
                {
                    if (!lexicalRanking.ContainsKey(match.FilePath))
                    {
                        lexicalRanking[match.FilePath] = rank++;
                    }
                }
            }
            catch
            {
                // Degradação graciosa para o comportamento antigo em caso de exceção no serviço
                lexicalRanking = null;
            }
        }

        if (lexicalRanking == null)
        {
            lexicalRanking = ComputeLegacyLexicalRanking(queryTokens, entriesWithSim);
        }

        // 3. Reciprocal Rank Fusion (RRF): score = 1/(60 + rank_sem) + (has_lex ? 1/(60 + rank_lex) : 0)
        const float k = 60f;
        var fusedResults = new List<SemanticSearchResult>(entriesWithSim.Count);

        foreach (var item in entriesWithSim)
        {
            var semRank = semanticRanking[item.RelativePath];
            var semScore = 1f / (k + semRank);

            var lexScore = lexicalRanking.TryGetValue(item.RelativePath, out var lexRank)
                ? 1f / (k + lexRank)
                : 0f;

            var finalScore = semScore + lexScore;

            // Sem excerto aqui de proposito: ler o disco antes do Take(topK) faria uma abertura
            // de arquivo por nota do cofre a cada consulta, e 99,9% dessas leituras seriam
            // descartadas na linha seguinte. Num cofre grande isso dominava o custo da busca.
            fusedResults.Add(new SemanticSearchResult(item.RelativePath, item.Title, "", finalScore));
        }

        // Corta primeiro, le o disco depois: so os topK sobreviventes tem excerto lido.
        var topResults = fusedResults
            .OrderByDescending(r => r.SimilarityScore)
            .Take(topK)
            .ToList();

        for (int i = 0; i < topResults.Count; i++)
        {
            var result = topResults[i];
            topResults[i] = result with { Excerpt = ReadExcerpt(vaultRootPath, result.RelativePath) };
        }

        return topResults;
    }

    /// <summary>Contador de leituras de excerto em disco, para os testes provarem que so os topK sao lidos.</summary>
    internal static int ExcerptReadCount;

    private static string ReadExcerpt(string vaultRootPath, string relativePath)
    {
        var fullPath = Path.Combine(vaultRootPath, relativePath);
        if (!File.Exists(fullPath))
        {
            return "";
        }

        Interlocked.Increment(ref ExcerptReadCount);

        try
        {
            return string.Join(" ", File.ReadLines(fullPath).Take(6));
        }
        catch
        {
            return "";
        }
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
            var fullPath = Path.Combine(vaultRootPath, note.RelativePath);
            if (File.Exists(fullPath))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(fullPath, ct);
                    contextBuilder.AppendLine($"--- INÍCIO DA NOTA: [[{note.Title}]] ---");
                    contextBuilder.AppendLine(content.Length > 2500 ? content[..2500] + "\n[...]" : content);
                    contextBuilder.AppendLine($"--- FIM DA NOTA ---");
                    contextBuilder.AppendLine();
                }
                catch { }
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
