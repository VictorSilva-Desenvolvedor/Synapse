using System.Text;
using System.Text.Json;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;

namespace Synapse.Brain.Providers;

/// <summary>
/// Provedor de embeddings vetoriais local e 100% offline via Ollama (ADR-009, Custo Zero).
/// Usa /api/embeddings com um modelo dedicado de embeddings (ex.: nomic-embed-text).
/// </summary>
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly BrainConfig _config;

    public string ModelName => _config.OllamaEmbeddingModel;

    public OllamaEmbeddingProvider(BrainConfig config, HttpClient? httpClient = null)
    {
        _config = config;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<float>();
        }

        var requestBody = new
        {
            model = _config.OllamaEmbeddingModel,
            prompt = text.Length > 8000 ? text[..8000] : text
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync($"{_config.OllamaEndpoint.TrimEnd('/')}/api/embeddings", content, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Não foi possível contatar o Ollama em {_config.OllamaEndpoint}. Verifique se o serviço está rodando. Detalhe: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Ollama retornou {(int)response.StatusCode} {response.StatusCode} ao gerar embedding: {errorBody}");
        }

        var jsonStr = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(jsonStr);

        if (!doc.RootElement.TryGetProperty("embedding", out var embArray) || embArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Resposta do Ollama sem campo 'embedding' válido: {jsonStr}");
        }

        var values = new float[embArray.GetArrayLength()];
        var idx = 0;
        foreach (var val in embArray.EnumerateArray())
        {
            values[idx++] = val.GetSingle();
        }

        return values;
    }
}
