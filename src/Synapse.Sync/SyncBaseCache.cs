using Synapse.Core.Ports;

namespace Synapse.Sync;

/// <summary>
/// Cache local do conteúdo da última versão sincronizada de cada nota (a "base" do merge de 3 vias,
/// RF-CONFLICT.2) - mantido fora do índice SQLite (que só guarda hash, não conteúdo) para não inflar o
/// banco com texto completo por nota. Espelha a estrutura de pastas do cofre sob uma raiz de cache,
/// inspecionável por um usuário técnico como qualquer outro arquivo.
/// </summary>
internal sealed class SyncBaseCache
{
    private readonly IFileSystem _fileSystem;
    private readonly string _cacheRoot;

    public SyncBaseCache(IFileSystem fileSystem, string cacheRoot)
    {
        _fileSystem = fileSystem;
        _cacheRoot = cacheRoot;
    }

    private string PathFor(string relativePath) => Path.Combine(_cacheRoot, relativePath);

    public async Task<string?> TryReadAsync(string relativePath, CancellationToken ct)
    {
        var path = PathFor(relativePath);
        return await _fileSystem.ExistsAsync(path, ct) ? await _fileSystem.ReadAllTextAsync(path, ct) : null;
    }

    public Task WriteAsync(string relativePath, string content, CancellationToken ct) =>
        _fileSystem.WriteAllTextAsync(PathFor(relativePath), content, ct);

    public Task DeleteAsync(string relativePath, CancellationToken ct) =>
        _fileSystem.DeleteAsync(PathFor(relativePath), ct);
}
