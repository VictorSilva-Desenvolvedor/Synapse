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
    public string GeminiModel { get; set; } = "gemini-1.5-flash";

    // Configurações do Ollama (Local & 100% Offline)
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.2";

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
