namespace Synapse.Host;

/// <summary>
/// Caminhos resolvidos de execução do Synapse Host (ADR-006, ADR-010).
/// </summary>
public sealed record SynapseHostPaths(string VaultPath, string DatabasePath)
{
    public static SynapseHostPaths Resolve(Sync.Config.SynapseConfig? savedConfig, IConfiguration? configuration)
    {
        var synapseSection = configuration?.GetSection("Synapse");

        var vaultPath = !string.IsNullOrWhiteSpace(savedConfig?.VaultPath)
            ? savedConfig.VaultPath
            : (!string.IsNullOrWhiteSpace(synapseSection?["VaultPath"])
                ? synapseSection["VaultPath"]!
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SynapseVault"));

        var dbPath = !string.IsNullOrWhiteSpace(savedConfig?.DatabasePath)
            ? savedConfig.DatabasePath
            : (!string.IsNullOrWhiteSpace(synapseSection?["DatabasePath"])
                ? synapseSection["DatabasePath"]!
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Synapse", "synapse.db"));

        return new SynapseHostPaths(vaultPath, dbPath);
    }
}
