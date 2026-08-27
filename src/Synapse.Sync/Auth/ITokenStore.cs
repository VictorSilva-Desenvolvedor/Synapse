namespace Synapse.Sync.Auth;

/// <summary>
/// Porta para armazenamento persistente e seguro de tokens do GitHub.
/// </summary>
public interface ITokenStore
{
    Task<GitHubToken?> LoadTokenAsync(CancellationToken ct = default);
    Task SaveTokenAsync(GitHubToken token, CancellationToken ct = default);
    Task ClearTokenAsync(CancellationToken ct = default);
}
