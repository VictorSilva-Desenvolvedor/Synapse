using Synapse.Core.Ports;

namespace Synapse.Sync;

/// <summary>Implementação de IFileSystem (Synapse.Core.Ports) sobre System.IO.</summary>
public sealed class LocalFileSystem : IFileSystem
{
    public Task<bool> ExistsAsync(string path, CancellationToken ct) => Task.FromResult(File.Exists(path));

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct) => File.ReadAllTextAsync(path, ct);

    public async Task WriteAllTextAsync(string path, string content, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, content, ct);
    }

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }
}
