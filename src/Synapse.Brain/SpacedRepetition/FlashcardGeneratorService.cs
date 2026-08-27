using System.Text;
using System.Text.Json;
using Synapse.Brain.Models;

namespace Synapse.Brain.SpacedRepetition;

/// <summary>
/// Serviço de geração automática de Flashcards para Repetição Espaçada a partir de notas usando Gemini (V7.1).
/// </summary>
public sealed class FlashcardGeneratorService
{
    private readonly HttpClient _httpClient;
    private readonly BrainConfig _config;

    public FlashcardGeneratorService(BrainConfig config, HttpClient? httpClient = null)
    {
        _config = config;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<IReadOnlyList<FlashcardItem>> GenerateCardsFromNoteAsync(
        string notePath,
        string noteContent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(noteContent)) return [];

        var apiKey = _config.GetEffectiveGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return GenerateFallbackCards(notePath, noteContent);
        }

        var prompt = @"Você é um especialista em Aprendizado Ativo e Repetição Espaçada (SuperMemo/Anki).
Analise a nota do Obsidian abaixo e extraia de 2 a 4 flashcards com perguntas claras e respostas diretas e concisas.
Responda ESTRITAMENTE em formato JSON com o seguinte formato:
[
  {
    ""question"": ""Pergunta clara e focada"",
    ""answer"": ""Resposta concisa e explicativa""
  }
]

Nota:
---
" + (noteContent.Length > 6000 ? noteContent[..6000] : noteContent) + "\n---";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                temperature = 0.2
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_config.GeminiModel}:generateContent?key={apiKey}";

        try
        {
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, ct);

            if (response.IsSuccessStatusCode)
            {
                var jsonStr = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(jsonStr);
                var candidates = doc.RootElement.GetProperty("candidates");
                if (candidates.GetArrayLength() > 0)
                {
                    var text = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var rawCards = JsonSerializer.Deserialize<List<RawCardDto>>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (rawCards != null && rawCards.Count > 0)
                        {
                            return rawCards.Select(c => new FlashcardItem
                            {
                                SourceNotePath = notePath,
                                Question = c.Question,
                                Answer = c.Answer
                            }).ToList();
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return GenerateFallbackCards(notePath, noteContent);
    }

    private static IReadOnlyList<FlashcardItem> GenerateFallbackCards(string notePath, string content)
    {
        var title = Path.GetFileNameWithoutExtension(notePath);
        return
        [
            new FlashcardItem
            {
                SourceNotePath = notePath,
                Question = $"Qual é o conceito principal de {title}?",
                Answer = content.Length > 200 ? content[..200] + "..." : content
            }
        ];
    }

    private sealed class RawCardDto
    {
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
    }
}
