using System.Text.Json;

namespace Synapse.Sync.Config;

/// <summary>
/// Gerenciador de persistência atômica da configuração local do Synapse.
/// </summary>
public sealed class SynapseConfigManager
{
    private readonly string _configFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SynapseConfigManager(string? configFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(configFilePath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appData, "Synapse");
            Directory.CreateDirectory(dir);
            _configFilePath = Path.Combine(dir, "synapse_config.json");
        }
        else
        {
            var dir = Path.GetDirectoryName(configFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _configFilePath = configFilePath;
        }
    }

    public async Task<SynapseConfig> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_configFilePath))
        {
            return new SynapseConfig
            {
                DatabasePath = Path.Combine(Path.GetDirectoryName(_configFilePath) ?? ".", "synapse.db")
            };
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configFilePath, ct);
            var config = JsonSerializer.Deserialize<SynapseConfig>(json, JsonOptions);
            if (config == null)
            {
                return new SynapseConfig();
            }

            if (string.IsNullOrWhiteSpace(config.DatabasePath))
            {
                config.DatabasePath = Path.Combine(Path.GetDirectoryName(_configFilePath) ?? ".", "synapse.db");
            }

            return config;
        }
        catch
        {
            return new SynapseConfig();
        }
    }

    public async Task SaveAsync(SynapseConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        var tempPath = $"{_configFilePath}.{Guid.NewGuid():N}.tmp";

        await File.WriteAllTextAsync(tempPath, json, ct);
        File.Move(tempPath, _configFilePath, overwrite: true);
    }

    public static bool Validate(SynapseConfig config, out List<string> validationErrors)
    {
        validationErrors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.VaultPath))
        {
            validationErrors.Add("O caminho do cofre do Obsidian é obrigatório.");
        }
        else if (!Directory.Exists(config.VaultPath))
        {
            validationErrors.Add("O diretório do cofre informado não existe.");
        }

        if (string.IsNullOrWhiteSpace(config.Owner))
        {
            validationErrors.Add("O nome de usuário ou organização do GitHub é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(config.Repository))
        {
            validationErrors.Add("O nome do repositório no GitHub é obrigatório.");
        }

        return validationErrors.Count == 0;
    }
}
