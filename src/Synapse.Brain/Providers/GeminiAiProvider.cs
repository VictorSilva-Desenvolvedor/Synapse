using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;

namespace Synapse.Brain.Providers;

/// <summary>
/// Provedor principal de inteligência artificial usando a API oficial do Google Gemini (Free Tier).
/// Modelo padrão fixado em "gemini-3.6-flash" (validado com resposta rápida e consistente).
/// O alias "gemini-flash-latest" foi avaliado como alternativa auto-atualizável, mas na prática
/// apresentou respostas muito lentas (~2 min) e falhas transitórias mais frequentes — por isso
/// se optou por uma versão fixa. Como o Google descontinua versões específicas periodicamente
/// (ex.: gemini-1.5-flash, gemini-2.5-flash já descontinuados), esse valor pode precisar ser
/// atualizado de novo no futuro. Suporta modo JSON estruturado nativo.
/// </summary>
public sealed class GeminiAiProvider : IBrainAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly BrainConfig _config;

    public string ProviderName => "Google Gemini (Free Tier)";

    public GeminiAiProvider(BrainConfig config, HttpClient? httpClient = null)
    {
        _config = config;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<AiStructuredNote> ProcessRawNoteAsync(
        string rawInput,
        IReadOnlyList<string> existingVaultNotes,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return new AiStructuredNote { Title = "Nota Vazia", BodyMarkdown = "" };
        }

        var apiKey = _config.GetEffectiveGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return FallbackHeuristicProcessing(rawInput, existingVaultNotes, "API Key do Gemini não configurada. Defina em Configurações ou variável GEMINI_API_KEY.");
        }

        var vaultNotesList = string.Join(", ", existingVaultNotes.Take(40));

        var prompt = $@"Você é o assistente inteligente de Segundo Cérebro (PKM) do Synapse para Obsidian.
Analise a anotação, ideia ou link abaixo e responda ESTRITAMENTE em formato JSON com o seguinte schema:
{{
  ""title"": ""Título conciso, elegante e descritivo para a nota no Obsidian"",
  ""category"": ""Conceito | Ideia | Referencia | Projeto | Tarefa | Resumo"",
  ""tags"": [""tag1"", ""tag2"", ""tag3""],
  ""summary"": ""Resumo executivo de 1 a 2 frases"",
  ""keyPoints"": [""Ponto principal 1"", ""Ponto principal 2"", ""Ponto principal 3""],
  ""bodyMarkdown"": ""Texto formatado em Markdown com subtítulos, bullet points e explicações claras"",
  ""suggestedConnections"": [""Nomes de notas do cofre que têm forte relação semântica com este conteúdo""]
}}

Notas já existentes no cofre do usuário para sugerir conexões: [{vaultNotesList}]

Conteúdo bruto a processar:
---
{rawInput}
---";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                temperature = 0.3
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
                    var rawText = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                    if (!string.IsNullOrWhiteSpace(rawText))
                    {
                        var cleanJson = CleanJsonString(rawText);
                        var parsed = JsonSerializer.Deserialize<AiStructuredNote>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Title))
                        {
                            return parsed;
                        }
                    }
                }
            }
        }
        catch
        {
            // Em caso de falha de conexão com a API, aplica fallback estruturado
        }

        return FallbackHeuristicProcessing(rawInput, existingVaultNotes);
    }

    public async Task<string> AskQuestionAsync(string prompt, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var apiKey = _config.GetEffectiveGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "API Key do Gemini não configurada. Defina em Configurações ou na variável de ambiente GEMINI_API_KEY.");
        }

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_config.GeminiModel}:generateContent?key={apiKey}";
        var requestJson = JsonSerializer.Serialize(requestBody);

        const int maxAttempts = 2;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
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
                            return text;
                        }
                    }

                    lastError = new InvalidOperationException("A API do Gemini respondeu com sucesso, mas sem texto utilizável na resposta.");
                }
                else if ((int)response.StatusCode >= 500)
                {
                    // Erro transitório do servidor: elegível para retry.
                    lastError = new InvalidOperationException($"Gemini retornou {(int)response.StatusCode} {response.StatusCode}.");
                }
                else
                {
                    // Erro do cliente (chave inválida, modelo inexistente, etc.) — não adianta tentar de novo.
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    throw new InvalidOperationException($"Gemini retornou {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException && ex is not OperationCanceledException)
            {
                // Falha de rede/transporte: elegível para retry.
                lastError = ex;
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(1500, ct);
            }
        }

        throw new InvalidOperationException(
            $"Não foi possível obter resposta do Gemini após {maxAttempts} tentativas. Detalhe: {lastError?.Message}", lastError);
    }

    public async Task<string> GenerateMocAsync(
        string topic,
        IReadOnlyList<string> relatedNotes,
        CancellationToken ct = default)
    {
        var apiKey = _config.GetEffectiveGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return $"# MOC - {topic}\n\n## Notas do Tópico\n" + string.Join("\n", relatedNotes.Select(n => $"- [[{n}]]"));
        }

        var notesList = string.Join(", ", relatedNotes);
        var prompt = $@"Crie um Map of Content (MOC) completo em Markdown para o Obsidian sobre o tema '{topic}'.
Conecte as seguintes notas usando wikilinks [[Nome da Nota]]: [{notesList}].
Organize com introdução, grupos temáticos e conexões sugeridas.";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                temperature = 0.4
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
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
        }
        catch
        {
        }

        return $"# MOC - {topic}\n\n## Notas do Tópico\n" + string.Join("\n", relatedNotes.Select(n => $"- [[{n}]]"));
    }

    private static string CleanJsonString(string text)
    {
        var clean = text.Trim();
        // Remove code fences markdown ```json ... ``` se o modelo tiver incluído
        if (clean.StartsWith("```"))
        {
            clean = Regex.Replace(clean, @"^```(?:json)?\s*", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\s*```$", "", RegexOptions.IgnoreCase);
        }
        return clean.Trim();
    }

    private static AiStructuredNote FallbackHeuristicProcessing(string rawInput, IReadOnlyList<string> existingNotes, string? warning = null)
    {
        var lines = rawInput.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var firstLine = lines.Length > 0 ? lines[0].TrimStart('#', ' ', '-') : "Nota Sem Título";
        var title = firstLine.Length > 50 ? firstLine[..47] + "..." : firstLine;

        var tags = new List<string> { "cerebro", "quick-capture" };
        if (warning != null) tags.Add("sem-gemini-key");

        return new AiStructuredNote
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Nova Ideia" : title,
            Category = "Ideia",
            Tags = tags,
            Summary = warning ?? firstLine,
            KeyPoints = lines.Take(3).ToList(),
            BodyMarkdown = rawInput,
            SuggestedConnections = existingNotes.Take(3).ToList()
        };
    }
}
