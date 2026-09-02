namespace Synapse.Search;

public sealed record VaultSearchResult(
    string FilePath,
    string Snippet,
    double Rank);
