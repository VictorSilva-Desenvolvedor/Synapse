namespace Synapse.Search;

public interface IVaultBulkIndexer
{
    Task<int> IndexVaultAsync(
        string vaultRootPath,
        IVaultSearchIndex searchIndex,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
