using Shouldly;
using Synapse.Agent;
using Synapse.Sync.Auth;
using Synapse.Sync.GitHub;

namespace Synapse.Tests.Agent;

public class RemoteAgentTokenResolverTests
{
    private readonly GitHubClientConfig _config = new()
    {
        Owner = "VictorSilva-Desenvolvedor",
        Repository = "Synapse-Vault",
        Branch = "main"
    };

    [Fact]
    public async Task EnsureTokenAsync_WhenTokenExistsInStore_ShouldReturnTrueWithoutReadingEnvVar()
    {
        var store = new InMemoryTokenStore();
        await store.SaveTokenAsync(new GitHubToken("ghp_already_configured"));

        var authManager = new GitHubAuthManager(store, _config);

        var result = await RemoteAgentTokenResolver.EnsureTokenAsync(authManager, store);

        result.ShouldBeTrue();
        var token = await store.LoadTokenAsync();
        token.ShouldNotBeNull();
        token.Token.ShouldBe("ghp_already_configured");
    }

    [Fact]
    public async Task EnsureTokenAsync_WhenTokenNotInStoreButEnvVarSet_ShouldSaveToStoreAndReturnTrue()
    {
        var store = new InMemoryTokenStore();
        var authManager = new GitHubAuthManager(store, _config);

        var previousEnv = Environment.GetEnvironmentVariable("SYNAPSE_REMOTE_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("SYNAPSE_REMOTE_TOKEN", "ghp_seeded_from_env_123");

            var result = await RemoteAgentTokenResolver.EnsureTokenAsync(authManager, store);

            result.ShouldBeTrue();
            var token = await store.LoadTokenAsync();
            token.ShouldNotBeNull();
            token.Token.ShouldBe("ghp_seeded_from_env_123");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SYNAPSE_REMOTE_TOKEN", previousEnv);
        }
    }

    [Fact]
    public async Task EnsureTokenAsync_WhenNeitherExists_ShouldReturnFalse()
    {
        var store = new InMemoryTokenStore();
        var authManager = new GitHubAuthManager(store, _config);

        var previousEnv = Environment.GetEnvironmentVariable("SYNAPSE_REMOTE_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("SYNAPSE_REMOTE_TOKEN", null);

            var result = await RemoteAgentTokenResolver.EnsureTokenAsync(authManager, store);

            result.ShouldBeFalse();
            var token = await store.LoadTokenAsync();
            token.ShouldBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SYNAPSE_REMOTE_TOKEN", previousEnv);
        }
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
}
