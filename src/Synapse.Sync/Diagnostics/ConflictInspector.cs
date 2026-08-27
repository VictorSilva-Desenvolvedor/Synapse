namespace Synapse.Sync.Diagnostics;

/// <summary>
/// Representa uma nota preservada no diretório de conflitos (RNF-2, US-UX.4).
/// </summary>
public sealed record ConflictItem(
    string FileName,
    string RelativePath,
    string FullPath,
    DateTimeOffset ModifiedAt,
    long FileSizeBytes);

/// <summary>
/// Inspetor de notas em conflito preservadas na pasta _conflitos/ do cofre.
/// </summary>
public static class ConflictInspector
{
    public static Task<IReadOnlyList<ConflictItem>> ListConflictsAsync(string vaultRootPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath) || !Directory.Exists(vaultRootPath))
        {
            return Task.FromResult<IReadOnlyList<ConflictItem>>([]);
        }

        var conflictsDir = Path.Combine(vaultRootPath, "_conflitos");
        if (!Directory.Exists(conflictsDir))
        {
            return Task.FromResult<IReadOnlyList<ConflictItem>>([]);
        }

        try
        {
            var dir = new DirectoryInfo(conflictsDir);
            var files = dir.GetFiles("*.*", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new ConflictItem(
                    FileName: f.Name,
                    RelativePath: Path.GetRelativePath(vaultRootPath, f.FullName).Replace('\\', '/'),
                    FullPath: f.FullName,
                    ModifiedAt: new DateTimeOffset(f.LastWriteTimeUtc),
                    FileSizeBytes: f.Length))
                .ToList();

            return Task.FromResult<IReadOnlyList<ConflictItem>>(files);
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<ConflictItem>>([]);
        }
    }
}
