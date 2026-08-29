using Synapse.Brain.Models;

namespace Synapse.Brain.Ports;

/// <summary>
/// Porta para execução de consultas semânticas e RAG contra o cofre do Obsidian.
/// </summary>
public interface IVaultBrainQuery
{
    Task<RagAnswer> AskVaultAsync(string question, string vaultRootPath, CancellationToken ct = default);

    Task<ChatTurnOutcome> ProcessChatTurnAsync(string userMessage, string vaultRootPath, CancellationToken ct = default);
}
