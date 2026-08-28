namespace Synapse.Brain.Models;

public sealed record NoteEmbeddingEntry(
    string RelativePath,
    string ContentHash,
    float[] Vector,
    DateTimeOffset UpdatedAt);

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
