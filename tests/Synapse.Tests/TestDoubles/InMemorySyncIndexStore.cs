using Synapse.Core.Ports;

namespace Synapse.Tests.TestDoubles;

/// <summary>
/// Dublê de ISyncIndexStore (Plano de Testes seção 6.1) — mesmas garantias de contrato do repositório
/// SQLite real (ex.: PeekNext não remove o item), só que em memória, para testes unitários mais rápidos.
/// Os testes de integração de Synapse.Data usam o SQLite real, não este dublê.
/// </summary>
public sealed class InMemorySyncIndexStore : ISyncIndexStore
{
    private readonly Dictionary<string, SyncedFileRecord> _byLocalPath = [];
    private readonly List<SyncQueueItem> _queue = [];
    private readonly List<ConflictRecord> _conflicts = [];
    private long _nextFileId = 1;
    private long _nextQueueId = 1;
    private string? _pageToken;

    public IReadOnlyList<ConflictRecord> Conflicts => _conflicts;

    public Task<SyncedFileRecord?> FindByLocalPathAsync(string localPath, CancellationToken ct) =>
        Task.FromResult(_byLocalPath.GetValueOrDefault(localPath));

    public Task<SyncedFileRecord?> FindByCloudFileIdAsync(string cloudFileId, CancellationToken ct) =>
        Task.FromResult(_byLocalPath.Values.FirstOrDefault(r => r.CloudFileId == cloudFileId));

    public Task UpsertAsync(SyncedFileRecord record, CancellationToken ct)
    {
        var id = _byLocalPath.TryGetValue(record.LocalPath, out var existing) ? existing.Id : _nextFileId++;
        _byLocalPath[record.LocalPath] = record with { Id = id };
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string localPath, CancellationToken ct)
    {
        _byLocalPath.Remove(localPath);
        return Task.CompletedTask;
    }

    public Task EnqueueAsync(SyncQueueItem item, CancellationToken ct)
    {
        _queue.Add(item with { Id = _nextQueueId++ });
        return Task.CompletedTask;
    }

    public Task<SyncQueueItem?> PeekNextAsync(CancellationToken ct) =>
        Task.FromResult(_queue.OrderBy(i => i.Id).FirstOrDefault());

    public Task MarkDoneAsync(long queueItemId, CancellationToken ct)
    {
        _queue.RemoveAll(i => i.Id == queueItemId);
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(long queueItemId, string error, CancellationToken ct)
    {
        var index = _queue.FindIndex(i => i.Id == queueItemId);
        if (index >= 0)
            _queue[index] = _queue[index] with { Attempts = _queue[index].Attempts + 1, LastError = error };

        return Task.CompletedTask;
    }

    public Task<string?> GetChangesPageTokenAsync(CancellationToken ct) => Task.FromResult(_pageToken);

    public Task SaveChangesPageTokenAsync(string pageToken, CancellationToken ct)
    {
        _pageToken = pageToken;
        return Task.CompletedTask;
    }

    public Task RecordConflictAsync(ConflictRecord record, CancellationToken ct)
    {
        _conflicts.Add(record);
        return Task.CompletedTask;
    }
}
