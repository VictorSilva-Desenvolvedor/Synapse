using Synapse.Brain.Services;

namespace Synapse.Brain.Models;

public sealed record NoteEmbeddingEntry(
    string RelativePath,
    string ContentHash,
    float[] Vector,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string>? Tokens = null,
    IReadOnlySet<string>? TokenSet = null,
    IReadOnlySet<string>? TitleTokenSet = null)
{
    public IReadOnlyList<string> Tokens { get; init; } = Tokens ?? [];
    public IReadOnlySet<string> TokenSet { get; init; } = TokenSet ?? new HashSet<string>(Tokens ?? [], StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> TitleTokenSet { get; init; } = TitleTokenSet ?? new HashSet<string>(VaultRagEngine.Tokenize(Path.GetFileNameWithoutExtension(RelativePath)), StringComparer.OrdinalIgnoreCase);
}

public sealed record SemanticSearchResult(
    string RelativePath,
    string Title,
    string Excerpt,
    float SimilarityScore);

public sealed record RagAnswer(
    string Question,
    string Answer,
    IReadOnlyList<SemanticSearchResult> Sources);

public sealed record ChatTurnOutcome(
    string ReplyMessage,
    string? SavedNotePath,
    IReadOnlyList<SemanticSearchResult> Sources);
