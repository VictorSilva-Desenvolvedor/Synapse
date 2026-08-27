using System.Text.RegularExpressions;

namespace Synapse.Sync.Metrics;

public sealed record VaultMetricsReport(
    int TotalNotes,
    int TotalFolders,
    long TotalWords,
    long TotalCharacters,
    int NotesCreatedLast7Days,
    int NotesCreatedLast30Days,
    int EstimatedReadingMinutes,
    IReadOnlyDictionary<string, int> CategoryCounts);

/// <summary>
/// Coletor de métricas, produtividade e estatísticas de escrita do cofre (V8.3, US-UX.5).
/// </summary>
public static class VaultMetricsCollector
{
    private static readonly Regex FrontmatterCategoryRegex = new(@"^categoria:\s*[""']?([^""'\r\n]+)[""']?", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex WordSplitRegex = new(@"\s+", RegexOptions.Compiled);

    public static async Task<VaultMetricsReport> CollectMetricsAsync(string vaultRootPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath) || !Directory.Exists(vaultRootPath))
        {
            return new VaultMetricsReport(0, 0, 0, 0, 0, 0, 0, new Dictionary<string, int>());
        }

        var files = Directory.GetFiles(vaultRootPath, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".obsidian") && !f.Contains(".synapse") && !f.Contains("_conflitos") && !f.Contains(".trash"))
            .ToList();

        var directories = Directory.GetDirectories(vaultRootPath, "*", SearchOption.AllDirectories)
            .Where(d => !d.Contains(".obsidian") && !d.Contains(".synapse") && !d.Contains("_conflitos") && !d.Contains(".trash"))
            .ToList();

        long totalWords = 0;
        long totalChars = 0;
        var created7Days = 0;
        var created30Days = 0;
        var now = DateTime.UtcNow;
        var cutoff7 = now.AddDays(-7);
        var cutoff30 = now.AddDays(-30);

        var categories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTimeUtc >= cutoff7 || fileInfo.LastWriteTimeUtc >= cutoff7)
                {
                    created7Days++;
                }

                if (fileInfo.CreationTimeUtc >= cutoff30 || fileInfo.LastWriteTimeUtc >= cutoff30)
                {
                    created30Days++;
                }

                var content = await File.ReadAllTextAsync(file, ct);
                totalChars += content.Length;

                var words = WordSplitRegex.Split(content.Trim());
                if (words.Length > 0 && !string.IsNullOrWhiteSpace(words[0]))
                {
                    totalWords += words.Length;
                }

                var match = FrontmatterCategoryRegex.Match(content);
                var category = match.Success && match.Groups.Count > 1
                    ? match.Groups[1].Value.Trim()
                    : "Sem Categoria";

                categories[category] = categories.GetValueOrDefault(category) + 1;
            }
            catch { }
        }

        var readingMinutes = (int)Math.Ceiling(totalWords / 200.0);

        return new VaultMetricsReport(
            TotalNotes: files.Count,
            TotalFolders: directories.Count,
            TotalWords: totalWords,
            TotalCharacters: totalChars,
            NotesCreatedLast7Days: created7Days,
            NotesCreatedLast30Days: created30Days,
            EstimatedReadingMinutes: readingMinutes,
            CategoryCounts: categories);
    }
}
