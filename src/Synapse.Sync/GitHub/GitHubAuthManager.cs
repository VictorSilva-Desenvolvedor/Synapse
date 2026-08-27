using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Synapse.Core.Ports;
using Synapse.Sync.Auth;

namespace Synapse.Sync.GitHub;

/// <summary>
/// Gerencia a autenticação e validação de tokens com a GitHub API (RF-AUTH.1, RF-AUTH.3, ADR-003).
/// </summary>
public sealed class GitHubAuthManager
{
    private readonly ITokenStore _tokenStore;
    private readonly GitHubClientConfig _config;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubAuthManager>? _logger;

    public GitHubAuthManager(
        ITokenStore tokenStore,
        GitHubClientConfig config,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null,
        ILogger<GitHubAuthManager>? logger = null)
    {
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? new HttpClient();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>
    /// Retorna o token atual válido. Lança CloudAuthExpiredException se não houver token ou se estiver expirado.
    /// </summary>
    public async Task<string> GetValidTokenAsync(CancellationToken ct = default)
    {
        var token = await _tokenStore.LoadTokenAsync(ct);
        if (token == null || string.IsNullOrWhiteSpace(token.Token))
        {
            _logger?.LogWarning("Nenhum token do GitHub configurado.");
            throw new CloudAuthExpiredException("Nenhum token do GitHub configurado. Autenticação necessária.");
        }

        var now = _timeProvider.GetUtcNow();
        if (token.IsExpired(now))
        {
            _logger?.LogWarning("Token do GitHub expirado.");
            await _tokenStore.ClearTokenAsync(ct);
            throw new CloudAuthExpiredException("Token do GitHub expirado.");
        }

        return token.Token;
    }

    /// <summary>
    /// Valida o token contra o endpoint /user da GitHub REST API.
    /// </summary>
    public async Task<bool> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_config.BaseUrl.TrimEnd('/')}/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd(_config.UserAgent);

        using var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new CloudAuthExpiredException("Token do GitHub inválido ou revogado (401).");
        }

        return false;
    }

    public async Task SaveTokenAsync(string token, string tokenType = "Bearer", DateTimeOffset? expiresAt = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var githubToken = new GitHubToken(token, tokenType, expiresAt);
        await _tokenStore.SaveTokenAsync(githubToken, ct);
    }

    public async Task RevokeAsync(CancellationToken ct = default)
    {
        await _tokenStore.ClearTokenAsync(ct);
    }
}
