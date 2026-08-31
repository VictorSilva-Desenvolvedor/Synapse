using Synapse.Brain.Models;

namespace Synapse.Brain.Ports;

/// <summary>
/// Porta para persistência e carregamento do índice vetorial/lexical do cofre em disco.
/// </summary>
public interface IVaultIndexStore
{
    Task<Dictionary<string, NoteEmbeddingEntry>?> LoadAsync(string vaultRootPath, CancellationToken ct = default);
    Task SaveAsync(string vaultRootPath, IReadOnlyDictionary<string, NoteEmbeddingEntry> index, CancellationToken ct = default);
}
