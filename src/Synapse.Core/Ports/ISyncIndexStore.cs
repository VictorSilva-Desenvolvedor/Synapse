namespace Synapse.Core.Ports;

/// <summary>
/// Abstrai a persistência local (RF-SYNC.3/4). Implementação da v1: repositório SQLite em Synapse.Data.
/// </summary>
public interface ISyncIndexStore
{
    Task<SyncedFileRecord?> FindByLocalPathAsync(string localPath, CancellationToken ct);
    Task<SyncedFileRecord?> FindByCloudFileIdAsync(string cloudFileId, CancellationToken ct);
    Task UpsertAsync(SyncedFileRecord record, CancellationToken ct);
    Task RemoveAsync(string localPath, CancellationToken ct);

    Task EnqueueAsync(SyncQueueItem item, CancellationToken ct);

    /// <summary>
    /// Não remove o item da fila — só MarkDoneAsync/MarkFailedAsync fazem isso. Garante que uma queda do
    /// processo entre o Peek e o MarkDone deixe o item pendente para a próxima execução (sustenta RF-SYNC.4).
    /// </summary>
    Task<SyncQueueItem?> PeekNextAsync(CancellationToken ct);
    Task MarkDoneAsync(long queueItemId, CancellationToken ct);
    Task MarkFailedAsync(long queueItemId, string error, CancellationToken ct);

    Task<string?> GetChangesPageTokenAsync(CancellationToken ct);
    Task SaveChangesPageTokenAsync(string pageToken, CancellationToken ct);

    Task RecordConflictAsync(ConflictRecord record, CancellationToken ct);
}

public sealed record SyncedFileRecord(
    long Id,
    string LocalPath,
    string? CloudFileId,
    string ContentHash,
    DateTimeOffset LocalMtime,
    DateTimeOffset? CloudModifiedTime,
    DateTimeOffset LastSyncedAt,
    SyncStatus Status,
    string? CloudContentHash = null);

public enum SyncStatus { Synced, PendingUpload, PendingDownload, Conflict, Failed }

public sealed record SyncQueueItem(
    long Id,
    string FilePath,
    SyncEventType EventType,
    DateTimeOffset EnqueuedAt,
    int Attempts,
    string? LastError);

public enum SyncEventType { Created, Modified, Deleted, Renamed }

public sealed record ConflictRecord(
    string FilePath,
    string LocalVersionPath,
    string RemoteVersionPath,
    DateTimeOffset DetectedAt);
