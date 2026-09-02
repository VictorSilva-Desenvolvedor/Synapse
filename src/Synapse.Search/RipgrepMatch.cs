namespace Synapse.Search;

public sealed record RipgrepMatch(
    string FilePath,
    int LineNumber,
    string LineText,
    int MatchStart,
    int MatchEnd);
