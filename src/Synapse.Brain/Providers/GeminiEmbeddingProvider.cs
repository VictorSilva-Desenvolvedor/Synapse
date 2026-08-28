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

        try
        {
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, ct);

            if (response.IsSuccessStatusCode)
            {
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
            }
        }
        catch
        {
        }

        return GenerateDeterministicPseudoEmbedding(text);
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
