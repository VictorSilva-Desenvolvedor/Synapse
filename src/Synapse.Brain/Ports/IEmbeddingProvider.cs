namespace Synapse.Brain.Ports;

/// <summary>
/// Provedor de geração de vetores de embeddings para busca semântica no cofre.
/// </summary>
public interface IEmbeddingProvider
{
    string ModelName { get; }
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}
