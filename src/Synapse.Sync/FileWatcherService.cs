using Synapse.Core.Ports;

namespace Synapse.Sync;

/// <summary>
/// Implementação de IVaultWatcher (RF-SYNC.1) sobre System.IO.FileSystemWatcher. O callback do SO só
/// traduz e publica o evento bruto - nenhum I/O de rede ou disco pesado aqui (SAD seção 4), para não
/// bloquear o watcher e arriscar perder eventos sob carga.
/// </summary>
public sealed class FileWatcherService : IVaultWatcher
{
    private readonly string[] _watchedExtensions;
    private FileSystemWatcher? _watcher;
    private string? _rootPath;

    public event EventHandler<VaultChangeEvent>? Changed;

    public FileWatcherService(IEnumerable<string>? attachmentExtensions = null)
    {
        _watchedExtensions = (attachmentExtensions ?? [])
            .Select(NormalizeExtension)
            .Append(".md")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Start(string vaultRootPath)
    {
        if (_watcher is not null)
            throw new InvalidOperationException("O watcher já está em execução. Chame Stop() antes de reiniciar.");

        _rootPath = vaultRootPath;
        _watcher = new FileSystemWatcher(vaultRootPath)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
        };

        _watcher.Created += (_, e) => Raise(e.FullPath, SyncEventType.Created);
        _watcher.Changed += (_, e) => Raise(e.FullPath, SyncEventType.Modified);
        _watcher.Deleted += (_, e) => Raise(e.FullPath, SyncEventType.Deleted);
        // VaultChangeEvent (API - Synapse.md) carrega um unico caminho, sem "de/para" - renomear vira
        // exclusao do caminho antigo + criacao do novo, em vez de estender um contrato ja formalizado.
        _watcher.Renamed += (_, e) =>
        {
            Raise(e.OldFullPath, SyncEventType.Deleted);
            Raise(e.FullPath, SyncEventType.Created);
        };

        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        if (_watcher is null) return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
        _rootPath = null;
    }

    private void Raise(string fullPath, SyncEventType eventType)
    {
        if (_rootPath is null) return;
        if (!IsWatchedExtension(fullPath)) return;

        var relativePath = Path.GetRelativePath(_rootPath, fullPath).Replace('\\', '/');
        Changed?.Invoke(this, new VaultChangeEvent(relativePath, eventType));
    }

    private bool IsWatchedExtension(string path) =>
        _watchedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string NormalizeExtension(string ext) => ext.StartsWith('.') ? ext : "." + ext;

    public void Dispose() => Stop();
}
