using System.Text.RegularExpressions;

namespace Synapse.Brain.Graph;

public sealed record GraphNodeMetrics(
    string Title,
    string RelativePath,
    int InDegree,
    int OutDegree,
    int TotalConnections);

public sealed record VaultGraphReport(
    int TotalNotes,
    int TotalLinks,
    IReadOnlyList<GraphNodeMetrics> TopHubs,
    IReadOnlyList<string> IsolatedNotes,
    IReadOnlyList<string> DeadEnds);

/// <summary>
/// Analisador topológico do grafo de conhecimento do cofre do Obsidian (V7.2).
/// </summary>
public static class VaultGraphAnalyzer
{
    private static readonly Regex WikilinkRegex = new(@"\[\[([^\]\|#]+)(?:[\|#][^\]]+)?\]\]");

    public static async Task<VaultGraphReport> AnalyzeVaultAsync(string vaultRootPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath) || !Directory.Exists(vaultRootPath))
        {
            return new VaultGraphReport(0, 0, [], [], []);
        }

        var files = Directory.GetFiles(vaultRootPath, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".obsidian") && !f.Contains("_conflitos") && !f.Contains(".trash"))
            .ToList();

        var titleToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var outgoingLinks = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var incomingCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var title = Path.GetFileNameWithoutExtension(file);
            var relativePath = Path.GetRelativePath(vaultRootPath, file).Replace('\\', '/');
            titleToPath[title] = relativePath;
            outgoingLinks[title] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            incomingCount[title] = 0;
        }

        var totalLinks = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var sourceTitle = Path.GetFileNameWithoutExtension(file);
                var content = await File.ReadAllTextAsync(file, ct);
                var matches = WikilinkRegex.Matches(content);

                foreach (Match match in matches)
                {
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var target = match.Groups[1].Value.Trim();
                        if (titleToPath.ContainsKey(target) && !string.Equals(sourceTitle, target, StringComparison.OrdinalIgnoreCase))
                        {
                            if (outgoingLinks[sourceTitle].Add(target))
                            {
                                incomingCount[target] = incomingCount.GetValueOrDefault(target) + 1;
                                totalLinks++;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        var metricsList = new List<GraphNodeMetrics>();
        var isolated = new List<string>();
        var deadEnds = new List<string>();

        foreach (var (title, path) in titleToPath)
        {
            var outDeg = outgoingLinks[title].Count;
            var inDeg = incomingCount.GetValueOrDefault(title);
            var total = inDeg + outDeg;

            metricsList.Add(new GraphNodeMetrics(title, path, inDeg, outDeg, total));

            if (total == 0)
            {
                isolated.Add(title);
            }
            else if (inDeg > 0 && outDeg == 0)
            {
                deadEnds.Add(title);
            }
        }

        var topHubs = metricsList
            .OrderByDescending(m => m.TotalConnections)
            .Take(10)
            .ToList();

        return new VaultGraphReport(
            TotalNotes: titleToPath.Count,
            TotalLinks: totalLinks,
            TopHubs: topHubs,
            IsolatedNotes: isolated,
            DeadEnds: deadEnds);
    }
}
