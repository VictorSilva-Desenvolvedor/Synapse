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

        public MockHttpMessageHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseContent = responseContent;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
        var config = new BrainConfig { GeminiApiKey = "" };
        var provider = new GeminiAiProvider(config);

        var result = await provider.ProcessRawNoteAsync("Anotação Rápida de Teste", ["Nota Existente"]);

        result.Title.ShouldBe("Anotação Rápida de Teste");
        result.Tags.ShouldContain("cerebro");
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
            GeminiModel = "gemini-1.5-flash"
        };

        var provider = new GeminiAiProvider(config, httpClient);
        var moc = await provider.GenerateMocAsync("Arquitetura de Software", ["Arquitetura Hexagonal", "Domain Driven Design"]);

        moc.ShouldContain("# MOC - Arquitetura de Software");
        moc.ShouldContain("- [[Arquitetura Hexagonal]]");
    }
}
