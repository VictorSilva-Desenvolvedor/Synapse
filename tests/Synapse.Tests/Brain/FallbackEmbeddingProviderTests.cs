using Shouldly;
using Synapse.Brain.Ports;
using Synapse.Brain.Providers;

namespace Synapse.Tests.Brain;

public class FallbackEmbeddingProviderTests
{
    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        private readonly float[] _result;
        private readonly Exception? _throwOnCall;

        public string ModelName { get; }
        public int CallCount { get; private set; }

        public StubEmbeddingProvider(string name, float[] result, Exception? throwOnCall = null)
        {
            ModelName = name;
            _result = result;
            _throwOnCall = throwOnCall;
        }

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
        {
            CallCount++;
            if (_throwOnCall != null) throw _throwOnCall;
            return Task.FromResult(_result);
        }
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenPrimarySucceeds_NeverCallsFallback()
    {
        var primary = new StubEmbeddingProvider("gemini", [1f, 2f]);
        var fallback = new StubEmbeddingProvider("ollama", [9f, 9f]);
        var provider = new FallbackEmbeddingProvider(primary, fallback);

        var result = await provider.GenerateEmbeddingAsync("texto");

        result.ShouldBe(new[] { 1f, 2f });
        fallback.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenPrimaryFails_FallsBackToSecondary()
    {
        var primary = new StubEmbeddingProvider("gemini", [], new InvalidOperationException("cota excedida"));
        var fallback = new StubEmbeddingProvider("ollama", [3f, 4f]);
        var provider = new FallbackEmbeddingProvider(primary, fallback);

        var result = await provider.GenerateEmbeddingAsync("texto");

        result.ShouldBe(new[] { 3f, 4f });
        primary.CallCount.ShouldBe(1);
        fallback.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenBothFail_PropagatesFallbackException()
    {
        var primary = new StubEmbeddingProvider("gemini", [], new InvalidOperationException("erro primario"));
        var fallback = new StubEmbeddingProvider("ollama", [], new InvalidOperationException("erro fallback"));
        var provider = new FallbackEmbeddingProvider(primary, fallback);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await provider.GenerateEmbeddingAsync("texto"));

        ex.Message.ShouldBe("erro fallback");
    }
}
