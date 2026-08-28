using System.Security.Cryptography;
using System.Text;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;

namespace Synapse.Brain.Services;

/// <summary>
/// Motor de Busca Semântica Vetorial e RAG (Retrieval-Augmented Generation) para o cofre do Obsidian (V5.1).
/// </summary>
public sealed class VaultRagEngine : IVaultBrainQuery
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IBrainAiProvider _aiProvider;
    private readonly BrainConfig _config;
    private readonly Dictionary<string, NoteEmbeddingEntry> _index = new(StringComparer.OrdinalIgnoreCase);

    public VaultRagEngine(IEmbeddingProvider embeddingProvider, IBrainAiProvider aiProvider, BrainConfig? config = null)
    {
        _embeddingProvider = embeddingProvider;
        _aiProvider = aiProvider;
        _config = config ?? new BrainConfig();
    }

    public async Task IndexVaultAsync(string vaultRootPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath) || !Directory.Exists(vaultRootPath))
        {
            return;
        }

        var files = Directory.GetFiles(vaultRootPath, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".obsidian") && !f.Contains("_conflitos") && !f.Contains(".trash"))
            .ToList();

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var relativePath = Path.GetRelativePath(vaultRootPath, file).Replace('\\', '/');
                var text = await File.ReadAllTextAsync(file, ct);
                var hash = ComputeSha256(text);

                if (_index.TryGetValue(relativePath, out var existing) && existing.ContentHash == hash)
                {
                    continue; // Já indexado e inalterado
                }

                var vector = await _embeddingProvider.GenerateEmbeddingAsync(text, ct);
                _index[relativePath] = new NoteEmbeddingEntry(relativePath, hash, vector, DateTimeOffset.UtcNow);
            }
            catch
            {
                // Ignora falha em arquivo individual bloqueado
            }
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

        var queryVector = await _embeddingProvider.GenerateEmbeddingAsync(query, ct);
        var results = new List<SemanticSearchResult>();

        foreach (var (relativePath, entry) in _index)
        {
            var similarity = VectorMath.CosineSimilarity(queryVector, entry.Vector);
            var title = Path.GetFileNameWithoutExtension(relativePath);

            var fullPath = Path.Combine(vaultRootPath, relativePath);
            var excerpt = "";
            if (File.Exists(fullPath))
            {
                try
                {
                    var lines = File.ReadLines(fullPath).Take(6);
                    excerpt = string.Join(" ", lines);
                }
                catch { }
            }

            results.Add(new SemanticSearchResult(relativePath, title, excerpt, similarity));
        }

        return results
            .OrderByDescending(r => r.SimilarityScore)
            .Take(topK)
            .ToList();
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
                    _index[savedNotePath] = new NoteEmbeddingEntry(savedNotePath, hash, vector, DateTimeOffset.UtcNow);
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
