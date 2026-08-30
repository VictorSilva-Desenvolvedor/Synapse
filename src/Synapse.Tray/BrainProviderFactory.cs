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

    public static IBrainAiProvider CreateAiProvider(BrainConfig brainConfig, ILogger? logger = null)
    {
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
        var ollama = new OllamaEmbeddingProvider(brainConfig);

        if (string.IsNullOrWhiteSpace(brainConfig.GetEffectiveGeminiApiKey()))
        {
            return ollama;
        }

        var gemini = new GeminiEmbeddingProvider(brainConfig);
        return new FallbackEmbeddingProvider(gemini, ollama, logger);
    }
}
