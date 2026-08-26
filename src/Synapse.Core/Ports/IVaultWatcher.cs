namespace Synapse.Core.Ports;

/// <summary>
/// Abstrai o monitoramento do sistema de arquivos (RF-SYNC.1). Sem esta porta, Synapse.Sync dependeria
/// diretamente de System.IO.FileSystemWatcher, dificultando testar o SyncQueueProcessor sem disco real.
/// </summary>
public interface IVaultWatcher : IDisposable
{
    event EventHandler<VaultChangeEvent>? Changed;
    void Start(string vaultRootPath);
    void Stop();
}

public sealed record VaultChangeEvent(string RelativePath, SyncEventType EventType);
