namespace Synapse.Brain.SpacedRepetition;

public sealed class Sm2State
{
    public int RepetitionNumber { get; set; } = 0;
    public float EaseFactor { get; set; } = 2.5f;
    public int IntervalDays { get; set; } = 0;
    public DateTimeOffset NextReviewDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastReviewedDate { get; set; }
}

public sealed class FlashcardItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceNotePath { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public Sm2State State { get; set; } = new();
}
