using System.Text;
using System.Text.Json;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;

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

    public async Task<string> RefineAnswerAsync(
        string userQuestion,
        string rawDraft,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawDraft)) return string.Empty;

        var preSanitized = NoteFileWriter.SanitizeBodyMarkdown(rawDraft);

        var prompt = $@"Você é um refinador e sintetizador especialista para o assistente de Segundo Cérebro (Obsidian).
Sua missão é revisar o rascunho de resposta e entregar EXCLUSIVAMENTE a resposta final, direta, limpa e elegante para o usuário em Markdown.

Diretrizes Estritas:
1. ENTREGAR APENAS O QUE IMPORTA: Responda diretamente ao que o usuário perguntou ou confirmou.
2. ZERO RUÍDO: Remova qualquer meta-prompt, instrução de sistema ou dump de notas não solicitadas.
3. PRESERVAR WIKILINKS: Mantenha sempre as menções a notas no formato Obsidian [[Nome da Nota]].
4. FORMATO: Markdown limpo, direto e profissional.

Pergunta do usuário:
""{userQuestion}""

Rascunho a refinar:
---
{preSanitized}
---

Resposta final refinada:";

        try
        {
            var refined = await AskQuestionAsync(prompt, ct);
            return NoteFileWriter.SanitizeBodyMarkdown(refined.Trim());
        }
        catch
        {
            return NoteFileWriter.SanitizeBodyMarkdown(preSanitized);
        }
    }

    public async Task<ChatTurnResult> ProcessChatTurnAsync(
        string userMessage,
        IReadOnlyList<string> existingVaultNotes,
        IReadOnlyList<string> existingCategoryFolders,
        IReadOnlyList<SemanticSearchResult> relatedNotes,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return new ChatTurnResult
            {
                ShouldCapture = false,
                ShouldAnswer = false,
                ReplyMessage = "Olá! Como posso ajudar você hoje?"
            };
        }

        var vaultNotesList = existingVaultNotes.Count > 0
            ? string.Join(", ", existingVaultNotes.Take(30))
            : "Nenhuma nota";

        var categoryFoldersList = existingCategoryFolders.Count > 0
            ? string.Join(", ", existingCategoryFolders)
            : "Nenhuma pasta";

        var relatedNotesBuilder = new StringBuilder();
        if (relatedNotes.Count > 0)
        {
            foreach (var note in relatedNotes)
            {
                relatedNotesBuilder.AppendLine($"- [[{note.Title}]]: {note.Excerpt}");
            }
        }
        else
        {
            relatedNotesBuilder.AppendLine("Nenhuma nota diretamente relacionada.");
        }

        var prompt = $@"Você é o assistente inteligente de Segundo Cérebro do Synapse para Obsidian.
Analise a mensagem do usuário e responda ESTRITAMENTE em formato JSON com o seguinte schema:
{{
  ""shouldCapture"": true | false,
  ""title"": ""Título específico e conciso se shouldCapture=true, ou null"",
  ""category"": ""Categoria da nota se shouldCapture=true, ou null"",
  ""tags"": [""tag1"", ""tag2""],
  ""bodyMarkdown"": ""Conteúdo formatado em Markdown se shouldCapture=true, ou null"",
  ""keyPoints"": [""ponto 1"", ""ponto 2""],
  ""suggestedConnections"": [""Nota Relacionada""],
  ""shouldAnswer"": true | false,
  ""replyMessage"": ""Resposta direta ao usuário ou confirmação amigável do que foi salvo""
}}

Regras:
1. ShouldCapture=true quando houver qualquer informação nova ou fato a registrar (compromissos, tarefas, prazos, credenciais, anotações).
2. ShouldAnswer=true quando a mensagem for uma pergunta a ser respondida com base nas notas relacionadas abaixo (cite [[wikilinks]]). Consolide TODAS as notas relevantes numa resposta unica: se houver varias notas sobre o mesmo assunto (ex.: 'Lista de Amigos' e 'Lista de Amigos (1)'), liste os itens de todas elas, sem repetir itens iguais, e nunca descarte uma dizendo que esta 'vazia' ou 'pronta para receber dados'. Transcreva os dados encontrados (nomes, valores), nao descreva a estrutura da nota.
3. replyMessage deve ser uma confirmação curta do que foi salvo (se ShouldCapture=true e ShouldAnswer=false) ou resposta amigável (se small talk) ou a resposta à pergunta.

Contexto:
- Pastas/Categorias existentes: [{categoryFoldersList}]
- Notas no cofre: [{vaultNotesList}]
- Notas relacionadas (RAG):
{relatedNotesBuilder}

Mensagem do usuário:
---
{userMessage}
---";

        var requestBody = new
        {
            model = _config.OllamaModel,
            prompt,
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
                        var parsed = JsonSerializer.Deserialize<ChatTurnResult>(innerJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (parsed != null) return parsed;
                    }
                }

                throw new InvalidOperationException("O Ollama respondeu com sucesso, mas sem JSON utilizável na resposta.");
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
