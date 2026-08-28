using System.Text;
using System.Text.Json;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;

namespace Synapse.Brain.Providers;

/// <summary>
/// Provedor de IA local e 100% offline via Ollama (ADR-009, Custo Zero).
/// </summary>
public sealed class OllamaAiProvider : IBrainAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly BrainConfig _config;

    public string ProviderName => "Ollama (Local Offline)";

    public OllamaAiProvider(BrainConfig config, HttpClient? httpClient = null)
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

        var vaultNotesList = string.Join(", ", existingVaultNotes.Take(30));

        var prompt = $@"Você é o motor de inteligência do Synapse, um sistema de Segundo Cérebro para Obsidian.
Analise a entrada bruta abaixo e responda ESTRITAMENTE em formato JSON com a seguinte estrutura:
{{
  ""title"": ""Título conciso e descritivo"",
  ""category"": ""Conceito | Ideia | Referencia | Projeto | Resumo"",
  ""tags"": [""tag1"", ""tag2""],
  ""summary"": ""Resumo de 1-2 frases"",
  ""keyPoints"": [""Ponto chave 1"", ""Ponto chave 2""],
  ""bodyMarkdown"": ""Texto formatado em markdown com subtítulos e bullet points"",
  ""suggestedConnections"": [""Notas existentes relacionadas""]
}}

Notas já existentes no cofre para possível conexão: [{vaultNotesList}]

Entrada bruta do usuário:
---
{rawInput}
---";

        var requestBody = new
        {
            model = _config.OllamaModel,
            prompt = prompt,
            stream = false,
            format = "json"
        };

        try
        {
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_config.OllamaEndpoint.TrimEnd('/')}/api/generate", content, ct);

            if (response.IsSuccessStatusCode)
            {
                var jsonStr = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.TryGetProperty("response", out var respElement))
                {
                    var innerJson = respElement.GetString();
                    if (!string.IsNullOrWhiteSpace(innerJson))
                    {
                        var parsed = JsonSerializer.Deserialize<AiStructuredNote>(innerJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (parsed != null) return parsed;
                    }
                }
            }
        }
        catch
        {
            // Fallback para estruturação heurística local se o serviço do Ollama não responder
        }

        return FallbackHeuristicProcessing(rawInput, existingVaultNotes);
    }

    public async Task<string> AskQuestionAsync(string prompt, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var requestBody = new
        {
            model = _config.OllamaModel,
            prompt,
            stream = false
        };

        try
        {
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_config.OllamaEndpoint.TrimEnd('/')}/api/generate", content, ct);

            if (response.IsSuccessStatusCode)
            {
                var jsonStr = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.TryGetProperty("response", out var respElement))
                {
                    var text = respElement.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                throw new InvalidOperationException("O Ollama respondeu com sucesso, mas sem texto utilizável na resposta.");
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Ollama retornou {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Não foi possível contatar o Ollama em {_config.OllamaEndpoint}. Verifique se o serviço está rodando. Detalhe: {ex.Message}", ex);
        }
    }

    public async Task<string> GenerateMocAsync(
        string topic,
        IReadOnlyList<string> relatedNotes,
        CancellationToken ct = default)
    {
        var notesList = string.Join(", ", relatedNotes);
        var prompt = $@"Crie um Map of Content (MOC) em Markdown para o Obsidian sobre o tópico '{topic}'.
Conecte as seguintes notas usando wikilinks [[Nome da Nota]]: [{notesList}].
Organize com introdução, tópicos e conexões.";

        var requestBody = new
        {
            model = _config.OllamaModel,
            prompt = prompt,
            stream = false
        };

        try
        {
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_config.OllamaEndpoint.TrimEnd('/')}/api/generate", content, ct);

            if (response.IsSuccessStatusCode)
            {
                var jsonStr = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(jsonStr);
                if (doc.RootElement.TryGetProperty("response", out var respElement))
                {
                    return respElement.GetString() ?? $"# MOC - {topic}\n\n" + string.Join("\n", relatedNotes.Select(n => $"- [[{n}]]"));
                }
            }
        }
        catch
        {
        }

        return $"# MOC - {topic}\n\n## Notas do Tópico\n" + string.Join("\n", relatedNotes.Select(n => $"- [[{n}]]"));
    }

    private static AiStructuredNote FallbackHeuristicProcessing(string rawInput, IReadOnlyList<string> existingNotes)
    {
        var lines = rawInput.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var firstLine = lines.Length > 0 ? lines[0].TrimStart('#', ' ', '-') : "Nota Sem Título";
        var title = firstLine.Length > 50 ? firstLine[..47] + "..." : firstLine;

        return new AiStructuredNote
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Nova Ideia" : title,
            Category = "Ideia",
            Tags = ["cerebro", "quick-capture"],
            Summary = firstLine,
            KeyPoints = lines.Take(3).ToList(),
            BodyMarkdown = rawInput,
            SuggestedConnections = existingNotes.Take(3).ToList()
        };
    }
}
