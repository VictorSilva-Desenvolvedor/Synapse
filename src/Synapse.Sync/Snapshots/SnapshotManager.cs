using System.Security.Cryptography;
using System.Text;

namespace Synapse.Sync.Snapshots;

public sealed record NoteSnapshotEntry(
    string SnapshotFileName,
    DateTimeOffset Timestamp,
    string ContentHash,
    string FullSnapshotPath);

/// <summary>
/// Gerenciador de Snapshots e Histórico Local de Revisões de Notas (V8.1, US-DATA.1).
/// </summary>
public static class SnapshotManager
{
    private const string SnapshotsDirectoryName = ".synapse/snapshots";

    public static async Task<string?> SaveSnapshotAsync(
        string vaultRootPath,
        string relativePath,
        string content,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath) || string.IsNullOrWhiteSpace(relativePath)) return null;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..12];
        var timestamp = DateTimeOffset.UtcNow;
        var sanitizedRel = relativePath.Replace('\\', '_').Replace('/', '_');

        var snapshotDir = Path.Combine(vaultRootPath, SnapshotsDirectoryName, sanitizedRel);
        Directory.CreateDirectory(snapshotDir);

        var snapshotFileName = $"{timestamp:yyyyMMdd_HHmmss}_{hash}.snapshot";
        var fullSnapshotPath = Path.Combine(snapshotDir, snapshotFileName);

        // Evita duplicar se o último snapshot já tiver exatamente o mesmo hash
        var existing = Directory.GetFiles(snapshotDir, $"*_{hash}.snapshot");
        if (existing.Length > 0)
        {
            return existing[0];
        }

        await File.WriteAllTextAsync(fullSnapshotPath, content, Encoding.UTF8, ct);
        return fullSnapshotPath;
    }

    public static IReadOnlyList<NoteSnapshotEntry> GetSnapshots(string vaultRootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath) || string.IsNullOrWhiteSpace(relativePath)) return [];

        var sanitizedRel = relativePath.Replace('\\', '_').Replace('/', '_');
        var snapshotDir = Path.Combine(vaultRootPath, SnapshotsDirectoryName, sanitizedRel);

        if (!Directory.Exists(snapshotDir)) return [];

        var files = Directory.GetFiles(snapshotDir, "*.snapshot");
        var list = new List<NoteSnapshotEntry>();

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var parts = fileName.Split('_');
            if (parts.Length >= 3)
            {
                var datePart = parts[0] + "_" + parts[1];
                if (DateTimeOffset.TryParseExact(datePart, "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var dto))
                {
                    var hash = Path.GetFileNameWithoutExtension(parts[2]);
                    list.Add(new NoteSnapshotEntry(fileName, dto, hash, file));
                }
            }
        }

        return list.OrderByDescending(s => s.Timestamp).ToList();
    }

    public static async Task<bool> RestoreSnapshotAsync(
        string vaultRootPath,
        string relativePath,
        string snapshotFullPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(snapshotFullPath)) return false;

        var targetFullPath = Path.Combine(vaultRootPath, relativePath);
        var snapshotContent = await File.ReadAllTextAsync(snapshotFullPath, ct);

        // Antes de restaurar, salva um snapshot do estado atual
        if (File.Exists(targetFullPath))
        {
            var currentContent = await File.ReadAllTextAsync(targetFullPath, ct);
            await SaveSnapshotAsync(vaultRootPath, relativePath, currentContent, ct);
        }

        await File.WriteAllTextAsync(targetFullPath, snapshotContent, Encoding.UTF8, ct);
        return true;
    }
}
