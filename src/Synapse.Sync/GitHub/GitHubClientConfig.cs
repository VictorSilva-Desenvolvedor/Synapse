namespace Synapse.Sync.GitHub;

/// <summary>
/// Configuração de conexão com o repositório do GitHub (RF-AUTH.1, RF-AUTH.2, ADR-017).
/// </summary>
public sealed class GitHubClientConfig
{
    public string Owner { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public string BaseUrl { get; set; } = "https://api.github.com";
    public string UserAgent { get; set; } = "Synapse-Obsidian-Sync/1.0";
}
