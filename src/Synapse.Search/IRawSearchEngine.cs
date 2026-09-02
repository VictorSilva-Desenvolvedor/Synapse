namespace Synapse.Search;

public interface IRawSearchEngine
{
    IAsyncEnumerable<RipgrepMatch> SearchAsync(
        string vaultRootPath,
        string pattern,
        bool isRegex = false,
        CancellationToken ct = default);
}
