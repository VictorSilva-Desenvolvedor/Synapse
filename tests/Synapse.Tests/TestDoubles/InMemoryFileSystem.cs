using Synapse.Core.Ports;

namespace Synapse.Tests.TestDoubles;

/// <summary>Dublê de IFileSystem (Plano de Testes seção 6.1) — substitui System.IO nos testes.</summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> ExistsAsync(string path, CancellationToken ct) =>
        Task.FromResult(_files.ContainsKey(Normalize(path)));

    public Task<string> ReadAllTextAsync(string path, CancellationToken ct)
    {
        if (!_files.TryGetValue(Normalize(path), out var content))
            throw new FileNotFoundException($"Arquivo não encontrado no InMemoryFileSystem: {path}", path);

        return Task.FromResult(content);
    }

    public Task WriteAllTextAsync(string path, string content, CancellationToken ct)
    {
        _files[Normalize(path)] = content;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        _files.Remove(Normalize(path));
        return Task.CompletedTask;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
