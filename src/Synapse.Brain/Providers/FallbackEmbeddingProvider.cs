using Microsoft.Extensions.Logging;
using Synapse.Brain.Ports;

namespace Synapse.Brain.Providers;

/// <summary>
/// Combina dois IEmbeddingProvider: tenta o primário (normalmente Gemini) e cai para o
/// secundário (normalmente Ollama local) se a chamada falhar por qualquer motivo. Mesma
/// filosofia do FallbackAiProvider - ver ali para o racional completo.
/// </summary>
public sealed class FallbackEmbeddingProvider : IEmbeddingProvider
{
    private readonly IEmbeddingProvider _primary;
    private readonly IEmbeddingProvider _fallback;
    private readonly ILogger? _logger;

    public string ModelName => $"{_primary.ModelName} (fallback: {_fallback.ModelName})";

    public FallbackEmbeddingProvider(IEmbeddingProvider primary, IEmbeddingProvider fallback, ILogger? logger = null)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        try
        {
            return await _primary.GenerateEmbeddingAsync(text, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Provedor de embeddings primário ({Primary}) falhou, tentando fallback ({Fallback}).",
                _primary.ModelName, _fallback.ModelName);
            try
            {
                return await _fallback.GenerateEmbeddingAsync(text, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception fallbackEx)
            {
                throw new InvalidOperationException(
                    $"{_primary.ModelName} falhou: {ex.Message} | {_fallback.ModelName} também falhou: {fallbackEx.Message}",
                    fallbackEx);
            }
        }
    }
}
