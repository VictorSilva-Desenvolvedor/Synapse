namespace Synapse.Sync.Config;

/// <summary>
/// Perfil de cofre individual para sincronização multi-vault (V2.3).
/// </summary>
public sealed class VaultProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Principal";
    public string VaultPath { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Repository { get; set; } = "Synapse-Vault";
    public string Branch { get; set; } = "main";
    public string DatabasePath { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(VaultPath) &&
        !string.IsNullOrWhiteSpace(Owner) &&
        !string.IsNullOrWhiteSpace(Repository);
}

/// <summary>
/// Modelo de configuração do Synapse compartilhado entre Host e Tray (RF-AUTH.1, RF-AUTH.2, US-AUTH.1, V2.3).
/// </summary>
public sealed class SynapseConfig
{
    // Retrocompatibilidade para cofre padrão
    public string VaultPath { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Repository { get; set; } = "Synapse-Vault";
    public string Branch { get; set; } = "main";
    public string DatabasePath { get; set; } = string.Empty;
    public int DebounceMs { get; set; } = 2000;
    public int PollingIntervalSeconds { get; set; } = 60;
    public int ReconciliationIntervalMinutes { get; set; } = 15;

    // Configurações da IA Gemini (Segundo Cérebro)
    public string GeminiApiKey { get; set; } = string.Empty;
    public string GeminiModel { get; set; } = "gemini-3.6-flash";

    // Controle Remoto via GitHub Relay (Fase 1 e Fase 2)
    public bool RemoteControlEnabled { get; set; } = false;
    public int RemoteControlPollingIntervalSeconds { get; set; } = 10;
    public Dictionary<string, string> RemoteAllowedApps { get; set; } = new();
    public bool RemoteRequireConfirmationForSensitiveActions { get; set; } = true;
    public int RemoteConfirmationTimeoutSeconds { get; set; } = 30;

    // Suporte a Múltiplos Cofres (V2.3)
    public List<VaultProfile> Vaults { get; set; } = new();

    public bool IsConfigured =>
        (!string.IsNullOrWhiteSpace(VaultPath) && !string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repository)) ||
        (Vaults.Count > 0 && Vaults.Any(v => v.IsConfigured && v.IsEnabled));

    public IReadOnlyList<VaultProfile> GetActiveVaults()
    {
        if (Vaults.Count > 0)
        {
            return Vaults.Where(v => v.IsEnabled && v.IsConfigured).ToList();
        }

        if (!string.IsNullOrWhiteSpace(VaultPath) && !string.IsNullOrWhiteSpace(Owner))
        {
            return
            [
                new VaultProfile
                {
                    Id = "default",
                    Name = "Principal",
                    VaultPath = VaultPath,
                    Owner = Owner,
                    Repository = Repository,
                    Branch = Branch,
                    DatabasePath = DatabasePath,
                    IsEnabled = true
                }
            ];
        }

        return [];
    }
}
