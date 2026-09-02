namespace Synapse.Search;

public interface IVaultSearchIndex : IDisposable
{
    Task IndexFileAsync(string filePath, string content, CancellationToken ct = default);

    Task IndexBatchAsync(IEnumerable<(string FilePath, string Content)> batch, CancellationToken ct = default);

    Task RemoveFileAsync(string filePath, CancellationToken ct = default);

    IAsyncEnumerable<VaultSearchResult> SearchAsync(string query, int limit = 100, CancellationToken ct = default);

    Task<int> GetIndexedFileCountAsync(CancellationToken ct = default);
}
