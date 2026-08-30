using Microsoft.Extensions.Logging;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;

namespace Synapse.Brain.Providers;

/// <summary>
/// Combina dois IBrainAiProvider: tenta o primário (normalmente Gemini, na nuvem) e, se
/// qualquer chamada falhar por qualquer motivo (cota diária excedida, indisponibilidade,
/// rede), cai automaticamente para o secundário (normalmente Ollama, local e sem limite).
/// Não distingue o tipo de falha de propósito - qualquer erro do primário é motivo
/// suficiente para tentar o secundário, já que o objetivo é nunca deixar o usuário sem
/// resposta por causa de uma cota gratuita.
/// </summary>
public sealed class FallbackAiProvider : IBrainAiProvider
{
    private readonly IBrainAiProvider _primary;
    private readonly IBrainAiProvider _fallback;
    private readonly ILogger? _logger;

    public string ProviderName => $"{_primary.ProviderName} (fallback: {_fallback.ProviderName})";

    public FallbackAiProvider(IBrainAiProvider primary, IBrainAiProvider fallback, ILogger? logger = null)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _logger = logger;
    }

    private async Task<T> RunWithFallbackAsync<T>(
        string operationName,
        Func<IBrainAiProvider, Task<T>> operation,
        CancellationToken ct)
    {
        try
        {
            return await operation(_primary);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Provedor primário ({Primary}) falhou em {Operation}, tentando fallback ({Fallback}).",
                _primary.ProviderName, operationName, _fallback.ProviderName);
            return await operation(_fallback);
        }
    }

    public Task<AiStructuredNote> ProcessRawNoteAsync(
        string rawInput,
        IReadOnlyList<string> existingVaultNotes,
        CancellationToken ct = default) =>
        RunWithFallbackAsync(nameof(ProcessRawNoteAsync), p => p.ProcessRawNoteAsync(rawInput, existingVaultNotes, ct), ct);

    public Task<string> GenerateMocAsync(
        string topic,
        IReadOnlyList<string> relatedNotes,
        CancellationToken ct = default) =>
        RunWithFallbackAsync(nameof(GenerateMocAsync), p => p.GenerateMocAsync(topic, relatedNotes, ct), ct);

    public Task<string> AskQuestionAsync(string prompt, CancellationToken ct = default) =>
        RunWithFallbackAsync(nameof(AskQuestionAsync), p => p.AskQuestionAsync(prompt, ct), ct);

    public Task<ChatTurnResult> ProcessChatTurnAsync(
        string userMessage,
        IReadOnlyList<string> existingVaultNotes,
        IReadOnlyList<string> existingCategoryFolders,
        IReadOnlyList<SemanticSearchResult> relatedNotes,
        CancellationToken ct = default) =>
        RunWithFallbackAsync(
            nameof(ProcessChatTurnAsync),
            p => p.ProcessChatTurnAsync(userMessage, existingVaultNotes, existingCategoryFolders, relatedNotes, ct),
            ct);

    public Task<string> RefineAnswerAsync(
        string userQuestion,
        string rawDraft,
        CancellationToken ct = default) =>
        RunWithFallbackAsync(nameof(RefineAnswerAsync), p => p.RefineAnswerAsync(userQuestion, rawDraft, ct), ct);
}
