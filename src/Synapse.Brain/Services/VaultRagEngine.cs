using System.Security.Cryptography;
using System.Text;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;

namespace Synapse.Brain.Services;

/// <summary>
/// Motor de Busca Semântica Vetorial e RAG (Retrieval-Augmented Generation) para o cofre do Obsidian (V5.1).
/// </summary>
public sealed class VaultRagEngine
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IBrainAiProvider _aiProvider;
    private readonly Dictionary<string, NoteEmbeddingEntry> _index = new(StringComparer.OrdinalIgnoreCase);

    public VaultRagEngine(IEmbeddingProvider embeddingProvider, IBrainAiProvider aiProvider)
    {
        _embeddingProvider = embeddingProvider;
        _aiProvider = aiProvider;
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

        var prompt = $@"Você é o assistente inteligente de pesquisa do Segundo Cérebro do usuário no Obsidian.
Com base EXCLUSIVAMENTE no contexto das notas do cofre fornecidas abaixo, responda à pergunta de forma clara, precisa e bem estruturada em Markdown.
SEMPRE mencione as notas de onde você extraiu as informações utilizando wikilinks [[Nome da Nota]].

Notas do cofre relevantes:
{contextBuilder}

Pergunta do usuário:
""{question}""";

        // Chama o processamento de texto da IA
        var structured = await _aiProvider.ProcessRawNoteAsync(prompt, topNotes.Select(n => n.Title).ToList(), ct);
        var answer = string.IsNullOrWhiteSpace(structured.BodyMarkdown)
            ? structured.Summary
            : structured.BodyMarkdown;

        return new RagAnswer(question, answer, topNotes);
    }

    private static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }
}
