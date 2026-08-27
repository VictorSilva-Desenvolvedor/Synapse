using System.Text.RegularExpressions;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;

namespace Synapse.Brain.Graph;

public sealed record SemanticBridgeSuggestion(
    string NoteATitle,
    string NoteBTitle,
    string NoteAPath,
    string NoteBPath,
    float SimilarityScore,
    string SuggestionReason);

/// <summary>
/// Sugere proativamente novas pontes de conhecimento interdisciplinares entre notas sem links diretos (V7.3).
/// </summary>
public sealed class SemanticBridgeSuggester
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private static readonly Regex WikilinkRegex = new(@"\[\[([^\]\|#]+)(?:[\|#][^\]]+)?\]\]");

    public SemanticBridgeSuggester(IEmbeddingProvider embeddingProvider)
    {
        _embeddingProvider = embeddingProvider;
    }

    public async Task<IReadOnlyList<SemanticBridgeSuggestion>> FindBridgeSuggestionsAsync(
        string vaultRootPath,
        float minSimilarity = 0.70f,
        int maxSuggestions = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath) || !Directory.Exists(vaultRootPath)) return [];

        var files = Directory.GetFiles(vaultRootPath, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".obsidian") && !f.Contains("_conflitos") && !f.Contains(".trash"))
            .Take(50) // Amostra de notas para varredura ágil
            .ToList();

        var noteData = new List<(string Title, string RelativePath, string Content, float[] Vector, HashSet<string> ExistingLinks)>();

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var content = await File.ReadAllTextAsync(file, ct);
                var title = Path.GetFileNameWithoutExtension(file);
                var relativePath = Path.GetRelativePath(vaultRootPath, file).Replace('\\', '/');

                var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in WikilinkRegex.Matches(content))
                {
                    if (m.Success && m.Groups.Count > 1) links.Add(m.Groups[1].Value.Trim());
                }

                var vector = await _embeddingProvider.GenerateEmbeddingAsync(content, ct);
                noteData.Add((title, relativePath, content, vector, links));
            }
            catch { }
        }

        var suggestions = new List<SemanticBridgeSuggestion>();

        for (var i = 0; i < noteData.Count; i++)
        {
            for (var j = i + 1; j < noteData.Count; j++)
            {
                var a = noteData[i];
                var b = noteData[j];

                // Se já estão linkadas em qualquer direção, pula
                if (a.ExistingLinks.Contains(b.Title) || b.ExistingLinks.Contains(a.Title))
                {
                    continue;
                }

                var similarity = VectorMath.CosineSimilarity(a.Vector, b.Vector);
                if (similarity >= minSimilarity)
                {
                    var reason = $"Notas com alta afinidade semântica ({similarity * 100:F0}%) sem link direto.";
                    suggestions.Add(new SemanticBridgeSuggestion(a.Title, b.Title, a.RelativePath, b.RelativePath, similarity, reason));
                }
            }
        }

        return suggestions
            .OrderByDescending(s => s.SimilarityScore)
            .Take(maxSuggestions)
            .ToList();
    }
}
