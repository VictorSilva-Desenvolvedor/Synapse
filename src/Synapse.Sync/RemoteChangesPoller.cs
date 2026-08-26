using Synapse.Core.Ports;

namespace Synapse.Sync;

/// <summary>
/// Detecção incremental de mudanças remotas (RF-SYNC.2): changes.list com pageToken persistido, sem
/// varredura completa recorrente (ADR-004). PeriodicTimer independente, não compete por I/O local - só
/// publica eventos no mesmo fluxo que o SyncQueueProcessor consome (SAD seção 4).
/// </summary>
public sealed class RemoteChangesPoller
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

    private readonly ICloudProvider _cloudProvider;
    private readonly ISyncIndexStore _indexStore;
    private readonly Func<VaultChangeEvent, CancellationToken, Task> _enqueue;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _timeProvider;

    public RemoteChangesPoller(
        ICloudProvider cloudProvider,
        ISyncIndexStore indexStore,
        Func<VaultChangeEvent, CancellationToken, Task> enqueue,
        TimeSpan? interval = null,
        TimeProvider? timeProvider = null)
    {
        _cloudProvider = cloudProvider;
        _indexStore = indexStore;
        _enqueue = enqueue;
        _interval = interval ?? DefaultInterval;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Um ciclo de polling: percorre todas as páginas de changes.list disponíveis e persiste o pageToken ao final.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        var currentToken = await _indexStore.GetChangesPageTokenAsync(ct) ?? await _cloudProvider.GetStartPageTokenAsync(ct);
        string? newStartToken = null;

        while (currentToken is not null)
        {
            var page = await _cloudProvider.GetChangesAsync(currentToken, ct);

            foreach (var changedFile in page.ChangedFiles)
            {
                var relativePath = await ResolveRelativePathAsync(changedFile, ct);
                var eventType = changedFile.Trashed ? SyncEventType.Deleted : SyncEventType.Modified;
                await _enqueue(new VaultChangeEvent(relativePath, eventType), ct);
            }

            newStartToken ??= page.NewStartPageToken;
            currentToken = page.NextPageToken;
        }

        if (newStartToken is not null)
            await _indexStore.SaveChangesPageTokenAsync(newStartToken, ct);
    }

    private async Task<string> ResolveRelativePathAsync(CloudFile file, CancellationToken ct)
    {
        var existing = await _indexStore.FindByCloudFileIdAsync(file.Id, ct);
        return existing?.LocalPath ?? file.Name;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_interval, _timeProvider);
        do
        {
            await RunOnceAsync(ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
