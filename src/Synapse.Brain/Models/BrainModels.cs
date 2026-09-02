namespace Synapse.Brain.Models;

public enum AiProviderType
{
    Ollama,
    Gemini
}

public sealed class BrainConfig
{
    public AiProviderType ProviderType { get; set; } = AiProviderType.Gemini;

    // Configurações do Google Gemini (Free Tier)
    public string GeminiApiKey { get; set; } = string.Empty;
    public string GeminiModel { get; set; } = "gemini-3.6-flash";

    // Configurações do Ollama (Local & 100% Offline)
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    // llama3.2:3b em vez do 3.1:8b: o Ollama descarrega o modelo da VRAM apos ~5 min ocioso, entao
    // cada pergunta paga o recarregamento. Medido nesta maquina (GTX 1660 Super, 6 GB): 8b leva
    // 9,5 s so para carregar contra 2,3 s do 3b - com a inferencia em si custando ~40 ms nos dois.
    public string OllamaModel { get; set; } = "llama3.2:3b";
    public string OllamaEmbeddingModel { get; set; } = "nomic-embed-text";

    // Pastas de destino no cofre
    public string DefaultFolder { get; set; } = "Brain";
    public bool AutoCategorizeFolders { get; set; } = true;
    public bool EnableAutoLinking { get; set; } = true;

    public string GetEffectiveGeminiApiKey()
    {
        if (!string.IsNullOrWhiteSpace(GeminiApiKey)) return GeminiApiKey;
        return Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty;
    }
}

public sealed class AiStructuredNote
{
    public string Title { get; set; } = "Sem Titulo";
    public string Category { get; set; } = "Conceito";
    public List<string> Tags { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
    public List<string> KeyPoints { get; set; } = [];
    public string BodyMarkdown { get; set; } = string.Empty;
    public List<string> SuggestedConnections { get; set; } = [];
}

public sealed class ChatTurnResult
{
    public bool ShouldCapture { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? BodyMarkdown { get; set; }
    public List<string> KeyPoints { get; set; } = [];
    public List<string> SuggestedConnections { get; set; } = [];
    public bool ShouldAnswer { get; set; }
    public string ReplyMessage { get; set; } = string.Empty;
}
