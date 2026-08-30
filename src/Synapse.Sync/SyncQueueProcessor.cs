using Synapse.Core.Ports;

namespace Synapse.Sync;

/// <summary>
/// Único consumidor que aplica mudanças (SAD seção 4): único ponto que escreve no índice e chama
/// ICloudProvider. EnqueueAsync persiste no ISyncIndexStore antes de qualquer outra coisa (RF-SYNC.4 -
/// fila sobrevive a uma queda entre o enfileiramento e a confirmação); DrainAsync processa tudo que
/// estiver pendente, um item por vez, até a fila esvaziar ou um item falhar definitivamente.
/// </summary>
public sealed class SyncQueueProcessor
{
    private readonly ICloudProvider _cloudProvider;
    private readonly ISyncIndexStore _indexStore;
    private readonly IConflictResolver _conflictResolver;
    private readonly IFileSystem _fileSystem;
    private readonly SyncQueueProcessorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SyncBaseCache _baseCache;
    private readonly RecentSelfWriteTracker? _selfWriteTracker;

    public SyncQueueProcessor(
        ICloudProvider cloudProvider,
        ISyncIndexStore indexStore,
        IConflictResolver conflictResolver,
        IFileSystem fileSystem,
        SyncQueueProcessorOptions options,
        TimeProvider? timeProvider = null,
        RecentSelfWriteTracker? selfWriteTracker = null)
    {
        _cloudProvider = cloudProvider;
        _indexStore = indexStore;
        _conflictResolver = conflictResolver;
        _fileSystem = fileSystem;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _baseCache = new SyncBaseCache(fileSystem, options.BaseCacheRootPath);
        _selfWriteTracker = selfWriteTracker;
    }

    public Task EnqueueAsync(VaultChangeEvent evt, CancellationToken ct) =>
        _indexStore.EnqueueAsync(new SyncQueueItem(0, evt.RelativePath, evt.EventType, _timeProvider.GetUtcNow(), 0, null), ct);

    /// <summary>Processa tudo que estiver pendente na fila persistida, um item por vez, em ordem.</summary>
    public async Task DrainAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var item = await _indexStore.PeekNextAsync(ct);
            if (item is null) break;

            var succeeded = await ProcessQueueItemAsync(item, ct);
            if (succeeded)
                await _indexStore.MarkDoneAsync(item.Id, ct);
            else
                break; // esgotou as tentativas deste item - espera o proximo ciclo/sinal, nao gira em loop
        }
    }

    private async Task<bool> ProcessQueueItemAsync(SyncQueueItem item, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                await ProcessCoreAsync(item.FilePath, ct);
                return true;
            }
            catch (CloudAuthExpiredException ex)
            {
                // Renovacao de token e responsabilidade do GoogleDriveProvider antes de cada chamada
                // (RF-AUTH.3); se ainda assim chegou aqui, o refresh token esta invalido - nao adianta
                // tentar de novo automaticamente (fica para o fluxo de reconexao manual, RF-AUTH.1).
                await _indexStore.MarkFailedAsync(item.Id, ex.Message, ct);
                return false;
            }
            catch (Exception ex) when (ex is CloudQuotaExceededException or CloudTransientException)
            {
                attempt++;
                await _indexStore.MarkFailedAsync(item.Id, ex.Message, ct);

                if (attempt >= _options.MaxAttempts)
                    return false;

                await Task.Delay(ComputeBackoffDelay(attempt), _timeProvider, ct);
            }
        }
    }

    // RF-SYNC.6: base 1s, fator 2, teto 60s, com jitter para nao sincronizar retries de multiplos
    // arquivos no mesmo instante.
    private static TimeSpan ComputeBackoffDelay(int attempt)
    {
        var baseSeconds = Math.Min(60, Math.Pow(2, attempt - 1));
        var jitterMs = Random.Shared.Next(0, 250);
        return TimeSpan.FromSeconds(baseSeconds) + TimeSpan.FromMilliseconds(jitterMs);
    }

    private async Task ProcessCoreAsync(string relativePath, CancellationToken ct)
    {
        var existing = await _indexStore.FindByLocalPathAsync(relativePath, ct);
        var localFullPath = Path.Combine(_options.VaultRootPath, relativePath);
        var localExists = await _fileSystem.ExistsAsync(localFullPath, ct);

        if (!localExists)
        {
            await HandleLocalDeletionAsync(relativePath, existing, ct);
            return;
        }

        var localContent = await _fileSystem.ReadAllTextAsync(localFullPath, ct);
        var localHash = ContentHasher.Sha256(localContent);

        if (existing is null)
        {
            await UploadNewFileAsync(relativePath, localFullPath, localContent, localHash, ct);
            return;
        }

        var localChanged = localHash != existing.ContentHash;
        var remoteMetadata = existing.CloudFileId is not null
            ? await TryGetRemoteMetadataAsync(existing.CloudFileId, ct)
            : null;
        var remoteChanged = remoteMetadata is not null
            && !string.Equals(remoteMetadata.Md5Checksum, existing.CloudContentHash, StringComparison.Ordinal);

        if (!localChanged && !remoteChanged)
            return; // evento redundante - nada mudou de fato desde a ultima sincronizacao

        if (localChanged && remoteChanged)
        {
            await ResolveConflictAsync(relativePath, existing, localContent, remoteMetadata!, ct);
            return;
        }

        if (localChanged)
        {
            await UploadUpdateAsync(relativePath, localFullPath, existing, localContent, localHash, ct);
            return;
        }

        await DownloadUpdateAsync(relativePath, localFullPath, existing, remoteMetadata!, ct);
    }

    private async Task<CloudFile?> TryGetRemoteMetadataAsync(string cloudFileId, CancellationToken ct)
    {
        try
        {
            return await _cloudProvider.GetMetadataAsync(cloudFileId, ct);
        }
        catch (CloudNotFoundException)
        {
            return null; // arquivo removido remotamente - tratado como "nada novo do lado remoto" por ora
        }
    }

    private async Task UploadNewFileAsync(string relativePath, string localFullPath, string localContent, string localHash, CancellationToken ct)
    {
        var cloudFile = await _cloudProvider.UploadAsync(localFullPath, _options.RemoteFolderId, ct);
        var now = _timeProvider.GetUtcNow();

        await _indexStore.UpsertAsync(new SyncedFileRecord(0, relativePath, cloudFile.Id, localHash, now, cloudFile.ModifiedTime, now, SyncStatus.Synced, cloudFile.Md5Checksum), ct);
        await _baseCache.WriteAsync(relativePath, localContent, ct);
    }

    private async Task UploadUpdateAsync(string relativePath, string localFullPath, SyncedFileRecord existing, string localContent, string localHash, CancellationToken ct)
    {
        var cloudFile = await _cloudProvider.UpdateAsync(existing.CloudFileId!, localFullPath, ct);
        var now = _timeProvider.GetUtcNow();

        await _indexStore.UpsertAsync(existing with { ContentHash = localHash, LocalMtime = now, CloudModifiedTime = cloudFile.ModifiedTime, LastSyncedAt = now, Status = SyncStatus.Synced, CloudContentHash = cloudFile.Md5Checksum }, ct);
        await _baseCache.WriteAsync(relativePath, localContent, ct);
    }

    private async Task DownloadUpdateAsync(string relativePath, string localFullPath, SyncedFileRecord existing, CloudFile remoteMetadata, CancellationToken ct)
    {
        _selfWriteTracker?.MarkWritten(relativePath);
        await _cloudProvider.DownloadAsync(existing.CloudFileId!, localFullPath, ct);
        _selfWriteTracker?.MarkWritten(relativePath);
        var newContent = await _fileSystem.ReadAllTextAsync(localFullPath, ct);
        var newHash = ContentHasher.Sha256(newContent);
        var now = _timeProvider.GetUtcNow();

        await _indexStore.UpsertAsync(existing with { ContentHash = newHash, LocalMtime = now, CloudModifiedTime = remoteMetadata.ModifiedTime, LastSyncedAt = now, Status = SyncStatus.Synced, CloudContentHash = remoteMetadata.Md5Checksum }, ct);
        await _baseCache.WriteAsync(relativePath, newContent, ct);
    }

    private async Task HandleLocalDeletionAsync(string relativePath, SyncedFileRecord? existing, CancellationToken ct)
    {
        if (existing is null) return;

        if (existing.CloudFileId is not null)
            await _cloudProvider.DeleteAsync(existing.CloudFileId, ct);

        await _indexStore.RemoveAsync(relativePath, ct);
        await _baseCache.DeleteAsync(relativePath, ct);
    }

    // RF-CONFLICT.1-4: base = ultima versao sincronizada (cacheada localmente, SyncBaseCache); se as
    // mudancas nao se sobrepoem, o merge automatico resolve (RF-CONFLICT.2/3) e o resultado e enviado;
    // se colidem, preserva as duas versoes em _conflitos/ sem nunca sobrescrever o arquivo local
    // (RF-CONFLICT.4) - o "sync point" so avanca quando ha resolucao automatica de verdade.
    private async Task ResolveConflictAsync(string relativePath, SyncedFileRecord existing, string localContent, CloudFile remoteMetadata, CancellationToken ct)
    {
        var tempRemotePath = Path.Combine(_options.BaseCacheRootPath, ".tmp-remote", relativePath);
        await _cloudProvider.DownloadAsync(existing.CloudFileId!, tempRemotePath, ct);
        var remoteContent = await _fileSystem.ReadAllTextAsync(tempRemotePath, ct);
        await _fileSystem.DeleteAsync(tempRemotePath, ct);

        var baseContent = await _baseCache.TryReadAsync(relativePath, ct);

        if (baseContent is null)
        {
            await WriteConflictFilesAsync(relativePath, localContent, remoteContent, existing, ct);
            return;
        }

        var (baseFront, baseBody) = NoteContentSplitter.Split(baseContent);
        var (localFront, localBody) = NoteContentSplitter.Split(localContent);
        var (remoteFront, remoteBody) = NoteContentSplitter.Split(remoteContent);

        var bodyResult = _conflictResolver.TryMergeBody(baseBody, localBody, remoteBody);
        var frontResult = _conflictResolver.TryMergeFrontmatter(baseFront, localFront, remoteFront);

        if (bodyResult is MergeResult.Resolved resolvedBody && frontResult is MergeResult.Resolved resolvedFront)
        {
            var mergedContent = NoteContentSplitter.Join(resolvedFront.MergedContent, resolvedBody.MergedContent);
            var localFullPath = Path.Combine(_options.VaultRootPath, relativePath);
            _selfWriteTracker?.MarkWritten(relativePath);
            await _fileSystem.WriteAllTextAsync(localFullPath, mergedContent, ct);
            _selfWriteTracker?.MarkWritten(relativePath);

            await UploadUpdateAsync(relativePath, localFullPath, existing, mergedContent, ContentHasher.Sha256(mergedContent), ct);
            return;
        }

        await WriteConflictFilesAsync(relativePath, localContent, remoteContent, existing, ct);
    }

    private async Task WriteConflictFilesAsync(string relativePath, string localContent, string remoteContent, SyncedFileRecord existing, CancellationToken ct)
    {
        var timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss");
        var conflictDir = Path.Combine(_options.VaultRootPath, _options.ConflictsFolderName, relativePath);
        var localVersionPath = Path.Combine(conflictDir, $"local-{timestamp}.md");
        var remoteVersionPath = Path.Combine(conflictDir, $"remoto-{timestamp}.md");

        await _fileSystem.WriteAllTextAsync(localVersionPath, localContent, ct);
        await _fileSystem.WriteAllTextAsync(remoteVersionPath, remoteContent, ct);

        await _indexStore.RecordConflictAsync(new ConflictRecord(relativePath, localVersionPath, remoteVersionPath, _timeProvider.GetUtcNow()), ct);
        await _indexStore.UpsertAsync(existing with { Status = SyncStatus.Conflict }, ct);
    }
}
