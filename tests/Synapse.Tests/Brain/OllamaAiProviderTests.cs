using System.Net;
using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Providers;

namespace Synapse.Tests.Brain;

public class OllamaAiProviderTests
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
    public async Task AskQuestionAsync_WhenSuccessful_ShouldReturnPlainTextAnswer()
    {
        var handler = new MockHttpMessageHandler(@"{""response"":""Resposta local do Ollama.""}");
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { OllamaEndpoint = "http://localhost:11434", OllamaModel = "llama3" };
        var provider = new OllamaAiProvider(config, httpClient);

        var answer = await provider.AskQuestionAsync("Pergunta de teste");

        answer.ShouldBe("Resposta local do Ollama.");
    }

    [Fact]
    public async Task AskQuestionAsync_WhenServiceUnavailable_ShouldThrowWithoutLeakingPrompt()
    {
        var handler = new MockHttpMessageHandler("{\"error\":\"connection refused\"}", HttpStatusCode.ServiceUnavailable);
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { OllamaEndpoint = "http://localhost:11434", OllamaModel = "llama3" };
        var provider = new OllamaAiProvider(config, httpClient);

        var secretPrompt = "Notas do cofre relevantes: chave-secreta-456";

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await provider.AskQuestionAsync(secretPrompt));

        ex.Message.ShouldNotContain("chave-secreta-456");
    }
}
