using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;

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

        var prompt = $@"Você é o arquiteto inteligente de Segundo Cérebro (PKM) do Synapse para Obsidian.
Analise a anotação, contato, ideia, tarefa ou link abaixo e estruture o conhecimento com precisão.
Tome decisões lógicas sobre a categoria ('Pessoas', 'Tarefas', 'Ideias', 'Projetos', 'Conceito', 'Referencia') e formate o conteúdo com tabelas Markdown profissionais ou tópicos quando apropriado.
NUNCA inclua saudações, conversas ou meta-prompts no bodyMarkdown.

Responda ESTRITAMENTE em formato JSON com o seguinte schema:
{{
  ""title"": ""Título conciso, elegante e descritivo para a nota no Obsidian"",
  ""category"": ""Pessoas | Tarefas | Ideias | Projetos | Conceito | Referencia | Resumo"",
  ""tags"": [""tag1"", ""tag2"", ""tag3""],
  ""summary"": ""Resumo executivo de 1 a 2 frases"",
  ""keyPoints"": [""Ponto principal 1"", ""Ponto principal 2""],
  ""bodyMarkdown"": ""Texto formatado em Markdown limpo com tabelas, seções e bullet points"",
  ""suggestedConnections"": [""Nomes de notas do cofre com forte relação semântica""]
}}

Notas já existentes no cofre do usuário: [{vaultNotesList}]

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

        var modelName = GetEffectiveModelName();
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

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

        var modelName = GetEffectiveModelName();
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
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

    public async Task<string> RefineAnswerAsync(
        string userQuestion,
        string rawDraft,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawDraft))
        {
            return string.Empty;
        }

        var apiKey = _config.GetEffectiveGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return NoteFileWriter.SanitizeBodyMarkdown(rawDraft);
        }

        var preSanitized = NoteFileWriter.SanitizeBodyMarkdown(rawDraft);

        var prompt = $@"Você é um refinador e sintetizador especialista para o assistente de Segundo Cérebro (Obsidian).
Sua missão é revisar o rascunho de resposta e entregar EXCLUSIVAMENTE a resposta final, direta, limpa e elegante para o usuário em Markdown.

Diretrizes Estritas:
1. ENTREGAR APENAS O QUE IMPORTA: Responda diretamente ao que o usuário perguntou ou confirmou, com tom claro e prestativo.
2. ZERO RUÍDO:
   - Remova qualquer meta-prompt, instrução de sistema, introdução prolixa ou contexto desnecessário.
   - NUNCA repita 'Você é o assistente...', 'Notas do cofre relevantes:', 'Pergunta do usuário:' ou blocos brutos de notas.
3. PRESERVAR WIKILINKS: Mantenha sempre as menções a notas no formato Obsidian [[Nome da Nota]].
4. FORMATO: Markdown limpo, direto e profissional.

Pergunta do usuário:
""{userQuestion}""

Rascunho a refinar:
---
{preSanitized}
---

Resposta final refinada:";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                temperature = 0.2
            }
        };

        var modelName = GetEffectiveModelName();
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
        var requestJson = JsonSerializer.Serialize(requestBody);

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
                        return NoteFileWriter.SanitizeBodyMarkdown(text.Trim());
                    }
                }
            }
        }
        catch
        {
            // Em caso de falha transitória no passo de refinamento, devolve o rascunho sanitizado
        }

        return NoteFileWriter.SanitizeBodyMarkdown(preSanitized);
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

        var apiKey = _config.GetEffectiveGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "API Key do Gemini não configurada. Defina em Configurações ou na variável de ambiente GEMINI_API_KEY.");
        }

        var vaultNotesList = existingVaultNotes.Count > 0
            ? string.Join(", ", existingVaultNotes.Take(40))
            : "Nenhuma nota no cofre ainda";

        var categoryFoldersList = existingCategoryFolders.Count > 0
            ? string.Join(", ", existingCategoryFolders)
            : "Nenhuma pasta específica";

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
            relatedNotesBuilder.AppendLine("Nenhuma nota diretamente relacionada encontrada.");
        }

        var prompt = $@"Você é o arquiteto inteligente do Segundo Cérebro (PKM) do Synapse integrado ao Obsidian.
Sua missão é analisar a mensagem do usuário, compreender sua intenção e tomar a melhor decisão estrutural para o cofre.

Diretrizes de Tomada de Decisão:
1. CAPTURAR OU ORGANIZAR (ShouldCapture=true):
   - Quando o usuário informar fatos, ideias, contatos, tarefas, amigos, reuniões, dados de projetos ou pedir para criar áreas/listas/tabelas (ex.: 'tenho um amigo chamado felipe', 'crie uma área para salvar meus amigos', 'adicione na lista X').
   - Decisões estruturais:
     * ""category"": escolha uma pasta semântica lógica (ex.: 'Pessoas' para amigos/contatos/pessoas, 'Tarefas' para afazeres/prazos, 'Projetos' para iniciativas, 'Conceito' para definições, 'Ideias' para pensamentos rápidos). Se já existir uma pasta relevante ({categoryFoldersList}), use-a.
     * ""title"": título específico, conciso e elegante (ex.: 'Amigos', 'Lista de Amigos', 'Felipe', 'Planejamento Q4'). Nunca use títulos genéricos como 'Nova Anotação'.
     * ""tags"": tags relevantes sem '#' (ex.: ['pessoas', 'amigos', 'contatos']).
     * ""bodyMarkdown"": ESTRUTURA PROFISSIONAL EM MARKDOWN. Se for uma lista, área ou catálogo de informações, use tabelas Markdown elegantes (| Nome | Relação | Detalhes | Data |) e tópicos bem formatados. NUNCA inclua saudações ('Olá'), conversas, perguntas, meta-prompts ou repetições da instrução do usuário dentro do corpo da nota. Apenas os dados refinados e organizados.
     * ""keyPoints"": lista de pontos-chave sintetizados.
     * ""suggestedConnections"": nomes de notas do cofre para conexão com wikilinks [[...]].

2. RESPONDER COM BASE NO COFRE (ShouldAnswer=true):
   - Quando a mensagem for estritamente uma pergunta ou busca sobre notas existentes no cofre. Responda em ""replyMessage"" de forma clara citando as notas com [[Nome da Nota]].

3. CAMPO ""replyMessage"" (Resposta no chat):
   - Se ShouldCapture=true: Explique de forma breve, elegante e prestativa a decisão tomada no cofre (ex.: 'Criei a área de Pessoas e adicionei o Felipe na sua lista de amigos com uma tabela estruturada.').
   - Se apenas conversa/saudação: Responda amigavelmente sem capturar nada (ShouldCapture=false).
   - Se ShouldCapture=true e ShouldAnswer=true: Responda à dúvida e confirme o que foi organizado.

Responda ESTRITAMENTE em formato JSON com o seguinte schema:
{{
  ""shouldCapture"": true | false,
  ""title"": ""Título específico da nota se shouldCapture=true, ou null"",
  ""category"": ""Categoria/pasta da nota se shouldCapture=true, ou null"",
  ""tags"": [""tag1"", ""tag2""],
  ""bodyMarkdown"": ""Conteúdo Markdown puro e estruturado da nota se shouldCapture=true, ou null"",
  ""keyPoints"": [""ponto 1"", ""ponto 2""],
  ""suggestedConnections"": [""Nota Relacionada 1""],
  ""shouldAnswer"": true | false,
  ""replyMessage"": ""Resposta ou confirmação amigável da ação tomada""
}}

Contexto do cofre:
- Pastas/Categorias existentes no cofre: [{categoryFoldersList}]
- Notas existentes no cofre para conexões: [{vaultNotesList}]
- Notas relevantes encontradas por busca semântica (RAG):
{relatedNotesBuilder}

Mensagem do usuário:
---
{userMessage}
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

        var modelName = GetEffectiveModelName();
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
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
                        var rawText = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                        if (!string.IsNullOrWhiteSpace(rawText))
                        {
                            var cleanJson = CleanJsonString(rawText);
                            var parsed = JsonSerializer.Deserialize<ChatTurnResult>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (parsed != null)
                            {
                                return parsed;
                            }
                        }
                    }

                    lastError = new InvalidOperationException("A API do Gemini respondeu com sucesso, mas sem JSON utilizável na resposta.");
                }
                else if ((int)response.StatusCode >= 500)
                {
                    lastError = new InvalidOperationException($"Gemini retornou {(int)response.StatusCode} {response.StatusCode}.");
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    throw new InvalidOperationException($"Gemini retornou {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException && ex is not OperationCanceledException)
            {
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

        var modelName = GetEffectiveModelName();
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

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

    private string GetEffectiveModelName()
    {
        if (string.IsNullOrWhiteSpace(_config.GeminiModel) ||
            _config.GeminiModel.StartsWith("gemini-1.") ||
            _config.GeminiModel.StartsWith("gemini-2.0") ||
            _config.GeminiModel.StartsWith("gemini-2.5"))
        {
            return "gemini-3.6-flash";
        }
        return _config.GeminiModel;
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
