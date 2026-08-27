using Synapse.Sync.Auth;
using Synapse.Sync.GitHub;

namespace Synapse.Agent;

/// <summary>
/// Resolvedor de token dedicado para o Synapse Remote Agent.
/// </summary>
public static class RemoteAgentTokenResolver
{
    public static async Task<bool> EnsureTokenAsync(
        GitHubAuthManager authManager,
        ITokenStore dedicatedStore,
        CancellationToken ct = default)
    {
        var existing = await dedicatedStore.LoadTokenAsync(ct);
        if (existing != null && !string.IsNullOrWhiteSpace(existing.Token))
        {
            return true;
        }

        var envToken = Environment.GetEnvironmentVariable("SYNAPSE_REMOTE_TOKEN");
        if (string.IsNullOrWhiteSpace(envToken))
        {
            return false;
        }

        await authManager.SaveTokenAsync(envToken, ct: ct);
        return true;
    }
}
