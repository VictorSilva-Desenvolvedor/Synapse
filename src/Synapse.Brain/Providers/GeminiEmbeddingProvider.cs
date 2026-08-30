using System.Text;
using System.Text.Json;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;

namespace Synapse.Brain.Providers;

/// <summary>
/// Provedor de embeddings vetoriais usando o modelo text-embedding-004 da API do Google Gemini (Free Tier).
/// </summary>
public sealed class GeminiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly BrainConfig _config;

    public string ModelName => "gemini-embedding-001";

    public GeminiEmbeddingProvider(BrainConfig config, HttpClient? httpClient = null)
    {
        _config = config;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[768];
        }

        var apiKey = _config.GetEffectiveGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return GenerateDeterministicPseudoEmbedding(text);
        }

        var requestBody = new
        {
            model = "models/gemini-embedding-001",
            content = new
            {
                parts = new[]
                {
                    new { text = text.Length > 8000 ? text[..8000] : text }
                }
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent?key={apiKey}";

        // Falhas reais de rede/API (cota excedida, servico fora, etc.) sao relancadas em vez
        // de mascaradas com um embedding pseudo-aleatorio: isso permitia que o RAG continuasse
        // "funcionando" silenciosamente com busca semantica quebrada (vetores sem relacao real
        // com o texto) sempre que a cota do Gemini estourasse. Quem chama esta classe (ex.:
        // FallbackEmbeddingProvider) decide o que fazer com a falha - normalmente cair para um
        // embedding local de verdade via Ollama, nao para ruido determinístico.
        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(url, content, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Falha de rede ao gerar embedding via Gemini: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Gemini retornou {(int)response.StatusCode} {response.StatusCode} ao gerar embedding: {errorBody}");
        }

        var jsonStr = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(jsonStr);
        if (doc.RootElement.TryGetProperty("embedding", out var embObj) &&
            embObj.TryGetProperty("values", out var valuesArray))
        {
            var values = new float[valuesArray.GetArrayLength()];
            var idx = 0;
            foreach (var val in valuesArray.EnumerateArray())
            {
                values[idx++] = val.GetSingle();
            }
            return values;
        }

        throw new InvalidOperationException($"Resposta do Gemini sem campo 'embedding.values' válido: {jsonStr}");
    }

    private static float[] GenerateDeterministicPseudoEmbedding(string text)
    {
        // Fallback determinístico caso a API esteja sem chave ou offline
        var vector = new float[768];
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text));
        for (var i = 0; i < 768; i++)
        {
            var b = hash[i % hash.Length];
            vector[i] = (b - 128f) / 128f;
        }
        return vector;
    }
}
