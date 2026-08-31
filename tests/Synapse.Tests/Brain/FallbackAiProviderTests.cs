using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Providers;

namespace Synapse.Tests.Brain;

public class FallbackAiProviderTests
{
    private sealed class StubAiProvider : IBrainAiProvider
    {
        private readonly Exception? _throwOnCall;

        public string ProviderName { get; }
        public int CallCount { get; private set; }

        public StubAiProvider(string name, Exception? throwOnCall = null)
        {
            ProviderName = name;
            _throwOnCall = throwOnCall;
        }

        public Task<AiStructuredNote> ProcessRawNoteAsync(string rawInput, IReadOnlyList<string> existingVaultNotes, CancellationToken ct = default)
        {
            CallCount++;
            if (_throwOnCall != null) throw _throwOnCall;
            return Task.FromResult(new AiStructuredNote { Title = ProviderName });
        }

        public Task<string> GenerateMocAsync(string topic, IReadOnlyList<string> relatedNotes, CancellationToken ct = default)
        {
            CallCount++;
            if (_throwOnCall != null) throw _throwOnCall;
            return Task.FromResult(ProviderName);
        }

        public Task<string> AskQuestionAsync(string prompt, CancellationToken ct = default)
        {
            CallCount++;
            if (_throwOnCall != null) throw _throwOnCall;
            return Task.FromResult(ProviderName);
        }

        public Task<ChatTurnResult> ProcessChatTurnAsync(string userMessage, IReadOnlyList<string> existingVaultNotes, IReadOnlyList<string> existingCategoryFolders, IReadOnlyList<SemanticSearchResult> relatedNotes, CancellationToken ct = default)
        {
            CallCount++;
            if (_throwOnCall != null) throw _throwOnCall;
            return Task.FromResult(new ChatTurnResult { Title = ProviderName });
        }

        public Task<string> RefineAnswerAsync(string userQuestion, string rawDraft, CancellationToken ct = default)
        {
            CallCount++;
            if (_throwOnCall != null) throw _throwOnCall;
            return Task.FromResult(ProviderName);
        }
    }

    [Fact]
    public async Task AskQuestionAsync_WhenPrimarySucceeds_NeverCallsFallback()
    {
        var primary = new StubAiProvider("primario");
        var fallback = new StubAiProvider("fallback");
        var provider = new FallbackAiProvider(primary, fallback);

        var answer = await provider.AskQuestionAsync("pergunta");

        answer.ShouldBe("primario");
        primary.CallCount.ShouldBe(1);
        fallback.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task AskQuestionAsync_WhenPrimaryFails_FallsBackToSecondary()
    {
        var primary = new StubAiProvider("primario", new InvalidOperationException("Gemini retornou 429 TooManyRequests"));
        var fallback = new StubAiProvider("fallback");
        var provider = new FallbackAiProvider(primary, fallback);

        var answer = await provider.AskQuestionAsync("pergunta");

        answer.ShouldBe("fallback");
        primary.CallCount.ShouldBe(1);
        fallback.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task AskQuestionAsync_WhenBothFail_ThrowsCombinedExceptionWithBothErrors()
    {
        var primary = new StubAiProvider("Gemini", new InvalidOperationException("API key expirada ou 429"));
        var fallback = new StubAiProvider("Ollama", new InvalidOperationException("Conexão recusada em 11434"));
        var provider = new FallbackAiProvider(primary, fallback);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await provider.AskQuestionAsync("pergunta"));

        ex.Message.ShouldContain("Gemini falhou: API key expirada ou 429");
        ex.Message.ShouldContain("Ollama também falhou: Conexão recusada em 11434");
        ex.InnerException.ShouldNotBeNull();
        ex.InnerException.Message.ShouldBe("Conexão recusada em 11434");
    }

    [Fact]
    public async Task ProcessChatTurnAsync_WhenPrimaryFails_FallsBackToSecondary()
    {
        var primary = new StubAiProvider("primario", new InvalidOperationException("indisponivel"));
        var fallback = new StubAiProvider("fallback");
        var provider = new FallbackAiProvider(primary, fallback);

        var result = await provider.ProcessChatTurnAsync("mensagem", [], [], []);

        result.Title.ShouldBe("fallback");
    }

    [Fact]
    public async Task ProcessChatTurnAsync_WhenBothFail_ThrowsCombinedExceptionWithBothErrors()
    {
        var primary = new StubAiProvider("Gemini", new InvalidOperationException("API key inválida"));
        var fallback = new StubAiProvider("Ollama", new InvalidOperationException("Ollama offline"));
        var provider = new FallbackAiProvider(primary, fallback);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await provider.ProcessChatTurnAsync("mensagem", [], [], []));

        ex.Message.ShouldContain("Gemini falhou: API key inválida");
        ex.Message.ShouldContain("Ollama também falhou: Ollama offline");
        ex.InnerException.ShouldNotBeNull();
        ex.InnerException.Message.ShouldBe("Ollama offline");
    }

    [Fact]
    public async Task AskQuestionAsync_WhenCancelled_DoesNotTryFallback()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var primary = new StubAiProvider("primario", new OperationCanceledException(cts.Token));
        var fallback = new StubAiProvider("fallback");
        var provider = new FallbackAiProvider(primary, fallback);

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await provider.AskQuestionAsync("pergunta", cts.Token));

        fallback.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task RunWithFallbackAsync_WhenPrimaryFails_LogsWarning()
    {
        var primary = new StubAiProvider("Gemini", new InvalidOperationException("timeout"));
        var fallback = new StubAiProvider("Ollama");
        var logger = Substitute.For<ILogger>();
        var provider = new FallbackAiProvider(primary, fallback, logger);

        await provider.AskQuestionAsync("teste");

        logger.ReceivedWithAnyArgs(1).Log(
            LogLevel.Warning,
            default,
            default!,
            default,
            default!);
    }
}
