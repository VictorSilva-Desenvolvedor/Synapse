namespace Synapse.Sync.Auth;

/// <summary>
/// Representa o token de autenticação do GitHub (Personal Access Token ou OAuth).
/// </summary>
public sealed record GitHubToken(
    string Token,
    string TokenType = "Bearer",
    DateTimeOffset? ExpiresAt = null)
{
    public bool IsExpired(DateTimeOffset now)
    {
        return ExpiresAt.HasValue && now >= ExpiresAt.Value;
    }
}
