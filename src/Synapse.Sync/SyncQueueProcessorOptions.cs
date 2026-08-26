namespace Synapse.Sync;

public sealed record SyncQueueProcessorOptions(
    string VaultRootPath,
    string RemoteFolderId,
    string BaseCacheRootPath,
    string ConflictsFolderName = "_conflitos",
    int MaxAttempts = 8);
