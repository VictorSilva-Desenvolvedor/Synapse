using System.Net;
using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Providers;

namespace Synapse.Tests.Brain;

public class GeminiAiProviderTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;
        private readonly HttpStatusCode _statusCode;
        public int CallCount { get; private set; }

        public MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseContent = responseContent;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task ProcessRawNoteAsync_WhenValidJsonResponseFromGemini_ShouldParseStructuredNote()
    {
        var geminiRawResponse = @"{
  ""candidates"": [
    {
      ""content"": {
        ""parts"": [
          {
            ""text"": ""{\""title\"": \""Metodologia PARA\"", \""category\"": \""Conceito\"", \""tags\"": [\""produtividade\"", \""ti\""], \""summary\"": \""Organizacao por Projetos, Areas, Recursos e Arquivos\"", \""keyPoints\"": [\""Projetos com prazo\"", \""Areas continuas\""], \""bodyMarkdown\"": \""O metodo PARA divide informacoes em 4 categorias.\"", \""suggestedConnections\"": [\""Segundo Cerebro\""]}""
          }
        ]
      }
    }
  ]
}";

        var handler = new MockHttpMessageHandler(geminiRawResponse);
        var httpClient = new HttpClient(handler);

        var config = new BrainConfig
        {
            GeminiApiKey = "AIzaSyTestKey123",
            GeminiModel = "gemini-1.5-flash"
        };

        var provider = new GeminiAiProvider(config, httpClient);
        var result = await provider.ProcessRawNoteAsync("Resumo sobre o método PARA do Tiago Forte", ["Segundo Cerebro"]);

        result.Title.ShouldBe("Metodologia PARA");
        result.Category.ShouldBe("Conceito");
        result.Tags.ShouldContain("produtividade");
        result.KeyPoints.Count.ShouldBe(2);
        result.SuggestedConnections.ShouldContain("Segundo Cerebro");
    }

    [Fact]
    public async Task ProcessRawNoteAsync_WhenNoApiKey_ShouldUseHeuristicFallback()
    {
        var prevEnv = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
            var config = new BrainConfig { GeminiApiKey = "" };
            var provider = new GeminiAiProvider(config);

            var result = await provider.ProcessRawNoteAsync("Anotação Rápida de Teste", ["Nota Existente"]);

            result.Title.ShouldBe("Anotação Rápida de Teste");
            result.Tags.ShouldContain("cerebro");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", prevEnv);
        }
    }

    private class SequenceHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Content)> _responses;
        public int CallCount { get; private set; }

        public SequenceHttpMessageHandler(params (HttpStatusCode StatusCode, string Content)[] responses)
        {
            _responses = new Queue<(HttpStatusCode, string)>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var (statusCode, content) = _responses.Count > 0 ? _responses.Dequeue() : _responses.Last();
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private const string ValidTextResponse = @"{
  ""candidates"": [
    { ""content"": { ""parts"": [ { ""text"": ""Resposta de teste em Markdown."" } ] } }
  ]
}";

    [Fact]
    public async Task AskQuestionAsync_WhenSuccessful_ShouldReturnPlainTextAnswer()
    {
        var handler = new SequenceHttpMessageHandler((HttpStatusCode.OK, ValidTextResponse));
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { GeminiApiKey = "AIzaSyTestKey123", GeminiModel = "gemini-flash-latest" };
        var provider = new GeminiAiProvider(config, httpClient);

        var answer = await provider.AskQuestionAsync("Pergunta de teste");

        answer.ShouldBe("Resposta de teste em Markdown.");
        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task AskQuestionAsync_WhenFirstAttemptIs503_ShouldRetryOnceAndSucceed()
    {
        var handler = new SequenceHttpMessageHandler(
            (HttpStatusCode.ServiceUnavailable, "{\"error\":\"indisponivel\"}"),
            (HttpStatusCode.OK, ValidTextResponse));
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { GeminiApiKey = "AIzaSyTestKey123", GeminiModel = "gemini-flash-latest" };
        var provider = new GeminiAiProvider(config, httpClient);

        var answer = await provider.AskQuestionAsync("Pergunta de teste");

        answer.ShouldBe("Resposta de teste em Markdown.");
        handler.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task AskQuestionAsync_WhenAllAttemptsFail_ShouldThrowWithoutLeakingPrompt()
    {
        var handler = new SequenceHttpMessageHandler(
            (HttpStatusCode.ServiceUnavailable, "{\"error\":\"indisponivel\"}"),
            (HttpStatusCode.ServiceUnavailable, "{\"error\":\"indisponivel\"}"));
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { GeminiApiKey = "AIzaSyTestKey123", GeminiModel = "gemini-flash-latest" };
        var provider = new GeminiAiProvider(config, httpClient);

        var secretPrompt = "Notas do cofre relevantes: --- INÍCIO DA NOTA: [[api]] --- chave-secreta-123 --- FIM DA NOTA ---";

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await provider.AskQuestionAsync(secretPrompt));

        handler.CallCount.ShouldBe(2);
        ex.Message.ShouldNotContain("chave-secreta-123");
    }

    [Fact]
    public async Task AskQuestionAsync_When4xxError_ShouldFailImmediatelyWithoutRetry()
    {
        var handler = new SequenceHttpMessageHandler(
            (HttpStatusCode.NotFound, "{\"error\":{\"message\":\"models/x is not found\"}}"));
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { GeminiApiKey = "AIzaSyTestKey123", GeminiModel = "gemini-modelo-invalido" };
        var provider = new GeminiAiProvider(config, httpClient);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await provider.AskQuestionAsync("Pergunta de teste"));

        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public void GeminiModel_DefaultValue_ShouldNotBeAKnownDiscontinuedModel()
    {
        var config = new BrainConfig();
        config.GeminiModel.ShouldNotBe("gemini-1.5-flash");
        config.GeminiModel.ShouldNotBe("gemini-2.5-flash");
    }

    [Fact]
    public async Task GenerateMocAsync_WhenApiKeyConfigured_ShouldReturnMoc()
    {
        var geminiRawResponse = @"{
  ""candidates"": [
    {
      ""content"": {
        ""parts"": [
          {
            ""text"": ""# MOC - Arquitetura de Software\n\n## Conceitos\n- [[Arquitetura Hexagonal]]\n- [[Domain Driven Design]]""
          }
        ]
      }
    }
  ]
}";

        var handler = new MockHttpMessageHandler(geminiRawResponse);
        var httpClient = new HttpClient(handler);

        var config = new BrainConfig
        {
            GeminiApiKey = "AIzaSyTestKey123",
            GeminiModel = "gemini-3.6-flash"
        };

        var provider = new GeminiAiProvider(config, httpClient);
        var moc = await provider.GenerateMocAsync("Arquitetura de Software", ["Arquitetura Hexagonal", "Domain Driven Design"]);

        moc.ShouldContain("# MOC - Arquitetura de Software");
        moc.ShouldContain("- [[Arquitetura Hexagonal]]");
    }

    [Fact]
    public async Task ProcessChatTurnAsync_WhenShouldCapture_ShouldParseStructuredResult()
    {
        var captureResponseJson = @"{
  ""candidates"": [
    {
      ""content"": {
        ""parts"": [
          {
            ""text"": ""{\""shouldCapture\"": true, \""title\"": \""Demanda do Chefe — 2026-08-29 12h\"", \""category\"": \""Tarefa\"", \""tags\"": [\""trabalho\"", \""urgente\""], \""bodyMarkdown\"": \""Falar com o chefe sobre alinhamento de demandas.\"", \""keyPoints\"": [\""Alinhamento no almoço\""], \""suggestedConnections\"": [\""Reunioes\""], \""shouldAnswer\"": false, \""replyMessage\"": \""Anotado! Salvei a demanda com o chefe no seu cofre.\""}""
          }
        ]
      }
    }
  ]
}";
        var handler = new MockHttpMessageHandler(captureResponseJson);
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { GeminiApiKey = "AIzaSyTestKey123", GeminiModel = "gemini-3.6-flash" };
        var provider = new GeminiAiProvider(config, httpClient);

        var result = await provider.ProcessChatTurnAsync(
            "falei com meu chefe hoje tenho uma demanda amanha almoco",
            ["Reunioes"],
            ["Tarefa", "Projetos"],
            []);

        result.ShouldCapture.ShouldBeTrue();
        result.ShouldAnswer.ShouldBeFalse();
        result.Title.ShouldBe("Demanda do Chefe — 2026-08-29 12h");
        result.Category.ShouldBe("Tarefa");
        result.Tags.ShouldContain("trabalho");
        result.KeyPoints.ShouldContain("Alinhamento no almoço");
        result.ReplyMessage.ShouldContain("Anotado!");
    }

    [Fact]
    public async Task ProcessChatTurnAsync_WhenShouldAnswer_ShouldParseAnswerResult()
    {
        var answerResponseJson = @"{
  ""candidates"": [
    {
      ""content"": {
        ""parts"": [
          {
            ""text"": ""{\""shouldCapture\"": false, \""title\"": null, \""category\"": null, \""tags\"": [], \""bodyMarkdown\"": null, \""keyPoints\"": [], \""suggestedConnections\"": [], \""shouldAnswer\"": true, \""replyMessage\"": \""Sua demanda com o chefe é amanhã às 12h, conforme anotado em [[Demanda do Chefe]].\""}""
          }
        ]
      }
    }
  ]
}";
        var handler = new MockHttpMessageHandler(answerResponseJson);
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { GeminiApiKey = "AIzaSyTestKey123", GeminiModel = "gemini-3.6-flash" };
        var provider = new GeminiAiProvider(config, httpClient);

        var result = await provider.ProcessChatTurnAsync(
            "que horas é minha demanda amanhã?",
            ["Demanda do Chefe"],
            ["Tarefa"],
            [new SemanticSearchResult("Brain/Tarefa/Demanda do Chefe.md", "Demanda do Chefe", "Demanda amanhã às 12h.", 0.92f)]);

        result.ShouldCapture.ShouldBeFalse();
        result.ShouldAnswer.ShouldBeTrue();
        result.ReplyMessage.ShouldContain("[[Demanda do Chefe]]");
        result.ReplyMessage.ShouldContain("12h");
    }

    [Fact]
    public async Task ProcessChatTurnAsync_WhenSmallTalk_ShouldReturnFriendlyReplyWithoutCaptureOrAnswer()
    {
        var smallTalkResponseJson = @"{
  ""candidates"": [
    {
      ""content"": {
        ""parts"": [
          {
            ""text"": ""{\""shouldCapture\"": false, \""title\"": null, \""category\"": null, \""tags\"": [], \""bodyMarkdown\"": null, \""keyPoints\"": [], \""suggestedConnections\"": [], \""shouldAnswer\"": false, \""replyMessage\"": \""De nada! Se precisar de mais alguma coisa, estou por aqui.\""}""
          }
        ]
      }
    }
  ]
}";
        var handler = new MockHttpMessageHandler(smallTalkResponseJson);
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { GeminiApiKey = "AIzaSyTestKey123", GeminiModel = "gemini-3.6-flash" };
        var provider = new GeminiAiProvider(config, httpClient);

        var result = await provider.ProcessChatTurnAsync("valeu", ["Nota 1"], ["Ideia"], []);

        result.ShouldCapture.ShouldBeFalse();
        result.ShouldAnswer.ShouldBeFalse();
        result.ReplyMessage.ShouldContain("De nada!");
    }

    [Fact]
    public async Task ProcessChatTurnAsync_WhenAllAttemptsFail_ShouldThrowWithoutLeakingPrompt()
    {
        var handler = new SequenceHttpMessageHandler(
            (HttpStatusCode.ServiceUnavailable, "{\"error\":\"indisponivel\"}"),
            (HttpStatusCode.ServiceUnavailable, "{\"error\":\"indisponivel\"}"));
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { GeminiApiKey = "AIzaSyTestKey123", GeminiModel = "gemini-3.6-flash" };
        var provider = new GeminiAiProvider(config, httpClient);

        var secretMessage = "mensagem-secreta-com-chave-4214j21k4j2k";

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await provider.ProcessChatTurnAsync(secretMessage, [], [], []));

        handler.CallCount.ShouldBe(2);
        ex.Message.ShouldNotContain(secretMessage);
    }

    [Fact]
    public void GeminiAiProvider_DefaultTimeouts_ShouldHave20sGeneralAnd45sChatTimeout()
    {
        var config = new BrainConfig();
        var provider = new GeminiAiProvider(config);

        provider.GeneralTimeout.ShouldBe(TimeSpan.FromSeconds(20));
        provider.ChatTimeout.ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public async Task ProcessChatTurnAsync_UsesDedicatedChatHttpClient_WhileOtherMethodsUseGeneralClient()
    {
        var chatHandler = new MockHttpMessageHandler(@"{
  ""candidates"": [
    { ""content"": { ""parts"": [ { ""text"": ""{\""shouldCapture\"": false, \""title\"": null, \""category\"": null, \""tags\"": [], \""bodyMarkdown\"": null, \""keyPoints\"": [], \""suggestedConnections\"": [], \""shouldAnswer\"": false, \""replyMessage\"": \""chat-ok\""}"" } ] } }
  ]
}");
        var generalHandler = new MockHttpMessageHandler(ValidTextResponse);

        var generalClient = new HttpClient(generalHandler) { Timeout = TimeSpan.FromSeconds(20) };
        var chatClient = new HttpClient(chatHandler) { Timeout = TimeSpan.FromSeconds(45) };

        var config = new BrainConfig { GeminiApiKey = "AIzaSyTestKey123", GeminiModel = "gemini-3.6-flash" };
        var provider = new GeminiAiProvider(config, generalClient, chatClient);

        var chatResult = await provider.ProcessChatTurnAsync("oi", [], [], []);
        chatResult.ReplyMessage.ShouldBe("chat-ok");
        chatHandler.CallCount.ShouldBe(1);
        generalHandler.CallCount.ShouldBe(0);

        var askResult = await provider.AskQuestionAsync("duvida");
        askResult.ShouldBe("Resposta de teste em Markdown.");
        generalHandler.CallCount.ShouldBe(1);
        chatHandler.CallCount.ShouldBe(1);
    }

    private class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }
        private readonly string _responseContent;

        public CapturingHttpMessageHandler(string responseContent)
        {
            _responseContent = responseContent;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public async Task ProcessChatTurnAsync_SendsContextualizedExcerptWithTableToAiProvider()
    {
        var captureHandler = new CapturingHttpMessageHandler(@"{
  ""candidates"": [
    { ""content"": { ""parts"": [ { ""text"": ""{\""shouldCapture\"": false, \""title\"": null, \""category\"": null, \""tags\"": [], \""bodyMarkdown\"": null, \""keyPoints\"": [], \""suggestedConnections\"": [], \""shouldAnswer\"": true, \""replyMessage\"": \""Seus amigos são Felipe...\""}"" } ] } }
  ]
}");
        var client = new HttpClient(captureHandler);
        var config = new BrainConfig { GeminiApiKey = "AIzaSyTestKey123", GeminiModel = "gemini-3.6-flash" };
        var provider = new GeminiAiProvider(config, client);

        var tableExcerpt = "| Nome | Relacao | Detalhes |\n| Felipe | Amigo | Engenheiro de Software |";
        var relatedNotes = new List<SemanticSearchResult>
        {
            new("Brain/Pessoas/Lista de Amigos.md", "Lista de Amigos", tableExcerpt, 0.95f)
        };

        var result = await provider.ProcessChatTurnAsync(
            "me diga minha lista de amigos",
            ["Lista de Amigos"],
            ["Pessoas"],
            relatedNotes);

        captureHandler.LastRequestBody.ShouldNotBeNull();
        // Prova que a IA recebe o trecho contextualizado com a tabela de amigos e os detalhes na íntegra
        captureHandler.LastRequestBody.ShouldContain("- [[Lista de Amigos]]: | Nome | Relacao | Detalhes |");
        captureHandler.LastRequestBody.ShouldContain("| Felipe | Amigo | Engenheiro de Software |");
    }
}
