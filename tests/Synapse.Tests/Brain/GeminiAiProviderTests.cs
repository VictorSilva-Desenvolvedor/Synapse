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
