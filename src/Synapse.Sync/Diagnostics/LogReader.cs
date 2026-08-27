using System.Text;

namespace Synapse.Sync.Diagnostics;

/// <summary>
/// Leitor utilitário de logs do Serilog com suporte a leitura concorrente (FileShare.ReadWrite) (US-UX.4).
/// </summary>
public static class LogReader
{
    public static async Task<IReadOnlyList<string>> ReadTailLinesAsync(string logFilePath, int maxLines = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
        {
            return [];
        }

        try
        {
            // Abre com FileShare.ReadWrite para não conflitar com a escrita do Serilog
            using var fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream, Encoding.UTF8);

            var lines = new List<string>();
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                lines.Add(line);
            }

            if (lines.Count <= maxLines)
            {
                return lines;
            }

            return lines.Skip(lines.Count - maxLines).ToList();
        }
        catch
        {
            return [];
        }
    }

    public static string? FindLatestLogFile(string logDirectory)
    {
        if (string.IsNullOrWhiteSpace(logDirectory) || !Directory.Exists(logDirectory))
        {
            return null;
        }

        try
        {
            var dir = new DirectoryInfo(logDirectory);
            var latestFile = dir.GetFiles("synapse-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            return latestFile?.FullName;
        }
        catch
        {
            return null;
        }
    }
}
