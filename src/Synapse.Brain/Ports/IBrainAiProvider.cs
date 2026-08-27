using Synapse.Brain.Models;

namespace Synapse.Brain.Ports;

/// <summary>
/// Contrato para provedores de inteligência artificial do Synapse Brain.
/// </summary>
public interface IBrainAiProvider
{
    string ProviderName { get; }

    Task<AiStructuredNote> ProcessRawNoteAsync(
        string rawInput,
        IReadOnlyList<string> existingVaultNotes,
        CancellationToken ct = default);

    Task<string> GenerateMocAsync(
        string topic,
        IReadOnlyList<string> relatedNotes,
        CancellationToken ct = default);
}
