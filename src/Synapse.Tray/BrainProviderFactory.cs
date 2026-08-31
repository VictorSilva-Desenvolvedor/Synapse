using Microsoft.Extensions.Logging;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Providers;
using Synapse.Sync.Config;

namespace Synapse.Tray;

/// <summary>
/// Monta os provedores de IA/embeddings do Segundo Cerebro a partir do SynapseConfig
/// persistido, aplicando o fallback automatico Gemini -> Ollama (ver FallbackAiProvider):
/// se houver chave Gemini configurada, tenta ela primeiro e cai para o Ollama local em
/// qualquer falha (cota diaria excedida, indisponibilidade, etc.). Sem chave Gemini, usa
/// Ollama direto - nao faz sentido tentar uma chamada que sabidamente vai falhar.
/// </summary>
internal static class BrainProviderFactory
{
    public static BrainConfig BuildBrainConfig(SynapseConfig config) => new()
    {
        GeminiApiKey = config.GeminiApiKey,
        GeminiModel = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-3.6-flash" : config.GeminiModel,
        OllamaEndpoint = string.IsNullOrWhiteSpace(config.OllamaEndpoint) ? "http://localhost:11434" : config.OllamaEndpoint,
        OllamaModel = string.IsNullOrWhiteSpace(config.OllamaModel) ? "llama3.1:8b" : config.OllamaModel,
        OllamaEmbeddingModel = string.IsNullOrWhiteSpace(config.OllamaEmbeddingModel) ? "nomic-embed-text" : config.OllamaEmbeddingModel
    };

    public static ILogger DefaultLogger { get; } = GetLogger("Brain");

    public static ILogger GetLogger(string category) => new ActivityLoggerAdapter(category);

    public static IBrainAiProvider CreateAiProvider(BrainConfig brainConfig, ILogger? logger = null)
    {
        logger ??= DefaultLogger;
        var ollama = new OllamaAiProvider(brainConfig);

        if (string.IsNullOrWhiteSpace(brainConfig.GetEffectiveGeminiApiKey()))
        {
            return ollama;
        }

        var gemini = new GeminiAiProvider(brainConfig);
        return new FallbackAiProvider(gemini, ollama, logger);
    }

    public static IEmbeddingProvider CreateEmbeddingProvider(BrainConfig brainConfig, ILogger? logger = null)
    {
        logger ??= DefaultLogger;
        var ollama = new OllamaEmbeddingProvider(brainConfig);

        if (string.IsNullOrWhiteSpace(brainConfig.GetEffectiveGeminiApiKey()))
        {
            return ollama;
        }

        var gemini = new GeminiEmbeddingProvider(brainConfig);
        return new FallbackEmbeddingProvider(gemini, ollama, logger);
    }

    private sealed class ActivityLoggerAdapter : ILogger
    {
        private readonly string _category;

        public ActivityLoggerAdapter(string category)
        {
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            var status = logLevel switch
            {
                LogLevel.Error or LogLevel.Critical => "Failed",
                LogLevel.Warning => "Warning",
                _ => "Success"
            };

            _ = Synapse.Core.Logging.SynapseActivityLogger.Instance.LogActionAsync(
                _category,
                logLevel.ToString(),
                details: message,
                status: status,
                errorMessage: exception?.Message);
        }
    }
}
