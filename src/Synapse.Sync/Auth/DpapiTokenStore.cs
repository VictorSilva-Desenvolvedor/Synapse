using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Synapse.Sync.Auth;

/// <summary>
/// Implementa ITokenStore utilizando DPAPI (ProtectedData, escopo CurrentUser) no Windows
/// para garantir que tokens do GitHub não sejam persistidos em texto plano (RF-AUTH.1, ADR-008).
/// </summary>
public sealed class DpapiTokenStore : ITokenStore
{
    private readonly string _filePath;
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("Synapse.GitHub.Auth.Entropy.v1");

    public DpapiTokenStore(string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appData, "Synapse");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "github_token.dat");
        }
        else
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _filePath = filePath;
        }
    }

    public async Task<GitHubToken?> LoadTokenAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var encryptedBytes = await File.ReadAllBytesAsync(_filePath, ct);
            if (encryptedBytes.Length == 0)
            {
                return null;
            }

            byte[] plainBytes;
            if (OperatingSystem.IsWindows())
            {
                plainBytes = ProtectedData.Unprotect(encryptedBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
            }
            else
            {
                plainBytes = encryptedBytes;
            }

            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<GitHubToken>(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveTokenAsync(GitHubToken token, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var json = JsonSerializer.Serialize(token);
        var plainBytes = Encoding.UTF8.GetBytes(json);

        byte[] encryptedBytes;
        if (OperatingSystem.IsWindows())
        {
            encryptedBytes = ProtectedData.Protect(plainBytes, OptionalEntropy, DataProtectionScope.CurrentUser);
        }
        else
        {
            encryptedBytes = plainBytes;
        }

        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(tempPath, encryptedBytes, ct);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    public Task ClearTokenAsync(CancellationToken ct = default)
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
        return Task.CompletedTask;
    }
}
