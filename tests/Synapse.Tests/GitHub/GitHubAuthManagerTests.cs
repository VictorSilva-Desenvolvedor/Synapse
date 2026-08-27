using System.Net;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Synapse.Core.Ports;
using Synapse.Sync.Auth;
using Synapse.Sync.GitHub;

namespace Synapse.Tests.GitHub;

public class GitHubAuthManagerTests
{
    private readonly InMemoryTokenStore _tokenStore = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero));
    private readonly GitHubClientConfig _config = new()
    {
        Owner = "VictorSilva-Desenvolvedor",
        Repository = "Synapse-Vault"
    };

    [Fact]
    public async Task GetValidTokenAsync_WhenTokenIsValid_ShouldReturnToken()
    {
        // Arrange
        var token = new GitHubToken("ghp_valid_token");
        await _tokenStore.SaveTokenAsync(token);

        var manager = new GitHubAuthManager(_tokenStore, _config, null, _timeProvider);

        // Act
        var result = await manager.GetValidTokenAsync();

        // Assert
        result.ShouldBe("ghp_valid_token");
    }

    [Fact]
    public async Task GetValidTokenAsync_WhenNoTokenSaved_ShouldThrowCloudAuthExpiredException()
    {
        var manager = new GitHubAuthManager(_tokenStore, _config, null, _timeProvider);

        await Should.ThrowAsync<CloudAuthExpiredException>(async () =>
        {
            await manager.GetValidTokenAsync();
        });
    }

    [Fact]
    public async Task GetValidTokenAsync_WhenTokenExpired_ShouldClearTokenAndThrow()
    {
        var expiredToken = new GitHubToken(
            Token: "ghp_expired",
            ExpiresAt: _timeProvider.GetUtcNow().AddHours(-1));
        await _tokenStore.SaveTokenAsync(expiredToken);

        var manager = new GitHubAuthManager(_tokenStore, _config, null, _timeProvider);

        await Should.ThrowAsync<CloudAuthExpiredException>(async () =>
        {
            await manager.GetValidTokenAsync();
        });

        (await _tokenStore.LoadTokenAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task ValidateTokenAsync_WhenGitHubReturns200_ShouldReturnTrue()
    {
        var handler = new MockHttpMessageHandler((req) =>
        {
            req.RequestUri!.ToString().ShouldContain("/user");
            req.Headers.Authorization!.Parameter.ShouldBe("ghp_test_token");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"login\": \"VictorSilva-Desenvolvedor\"}")
            });
        });

        using var httpClient = new HttpClient(handler);
        var manager = new GitHubAuthManager(_tokenStore, _config, httpClient, _timeProvider);

        var isValid = await manager.ValidateTokenAsync("ghp_test_token");

        isValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateTokenAsync_WhenGitHubReturns401_ShouldThrowCloudAuthExpiredException()
    {
        var handler = new MockHttpMessageHandler((req) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"message\": \"Bad credentials\"}")
            });
        });

        using var httpClient = new HttpClient(handler);
        var manager = new GitHubAuthManager(_tokenStore, _config, httpClient, _timeProvider);

        await Should.ThrowAsync<CloudAuthExpiredException>(async () =>
        {
            await manager.ValidateTokenAsync("ghp_invalid_token");
        });
    }

    private sealed class InMemoryTokenStore : ITokenStore
    {
        private GitHubToken? _token;

        public Task<GitHubToken?> LoadTokenAsync(CancellationToken ct = default) => Task.FromResult(_token);
        public Task SaveTokenAsync(GitHubToken token, CancellationToken ct = default)
        {
            _token = token;
            return Task.CompletedTask;
        }
        public Task ClearTokenAsync(CancellationToken ct = default)
        {
            _token = null;
            return Task.CompletedTask;
        }
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
