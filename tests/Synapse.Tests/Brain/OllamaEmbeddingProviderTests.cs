using System.Net;
using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Providers;

namespace Synapse.Tests.Brain;

public class OllamaEmbeddingProviderTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
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
    public async Task GenerateEmbeddingAsync_WhenSuccessful_ReturnsParsedVector()
    {
        var handler = new MockHttpMessageHandler(@"{""embedding"":[0.1,0.2,0.3]}");
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { OllamaEndpoint = "http://localhost:11434", OllamaEmbeddingModel = "nomic-embed-text" };
        var provider = new OllamaEmbeddingProvider(config, httpClient);

        var vector = await provider.GenerateEmbeddingAsync("texto de teste");

        vector.Length.ShouldBe(3);
        vector[0].ShouldBe(0.1f, 0.0001f);
        vector[1].ShouldBe(0.2f, 0.0001f);
        vector[2].ShouldBe(0.3f, 0.0001f);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenServiceUnavailable_ThrowsInvalidOperationException()
    {
        var handler = new MockHttpMessageHandler("{\"error\":\"connection refused\"}", HttpStatusCode.ServiceUnavailable);
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { OllamaEndpoint = "http://localhost:11434", OllamaEmbeddingModel = "nomic-embed-text" };
        var provider = new OllamaEmbeddingProvider(config, httpClient);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await provider.GenerateEmbeddingAsync("texto de teste"));
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenTextIsEmpty_ReturnsEmptyArrayWithoutCallingApi()
    {
        var handler = new MockHttpMessageHandler("should not be called", HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig();
        var provider = new OllamaEmbeddingProvider(config, httpClient);

        var vector = await provider.GenerateEmbeddingAsync("");

        vector.ShouldBeEmpty();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenResponseMissingEmbeddingField_ThrowsInvalidOperationException()
    {
        var handler = new MockHttpMessageHandler(@"{""nao_e_embedding"":true}");
        var httpClient = new HttpClient(handler);
        var config = new BrainConfig { OllamaEndpoint = "http://localhost:11434", OllamaEmbeddingModel = "nomic-embed-text" };
        var provider = new OllamaEmbeddingProvider(config, httpClient);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await provider.GenerateEmbeddingAsync("texto de teste"));
    }
}
