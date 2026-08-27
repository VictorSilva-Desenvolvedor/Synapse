using Shouldly;
using Synapse.Sync.Auth;

namespace Synapse.Tests.Auth;

public class DpapiTokenStoreTests : IDisposable
{
    private readonly string _tempFile;

    public DpapiTokenStoreTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"synapse-github-token-test-{Guid.NewGuid():N}.dat");
    }

    [Fact]
    public async Task SaveAndLoad_ShouldPersistAndDecryptGitHubTokenCorrectly()
    {
        // Arrange
        var store = new DpapiTokenStore(_tempFile);
        var token = new GitHubToken(
            Token: "ghp_1234567890abcdefghijklmnopqrstuvwxyz",
            TokenType: "Bearer",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(2));

        // Act
        await store.SaveTokenAsync(token);
        var loaded = await store.LoadTokenAsync();

        // Assert
        loaded.ShouldNotBeNull();
        loaded.Token.ShouldBe(token.Token);
        loaded.TokenType.ShouldBe(token.TokenType);
        loaded.ExpiresAt.ShouldNotBeNull();
        loaded.ExpiresAt!.Value.ToUnixTimeSeconds().ShouldBe(token.ExpiresAt!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task LoadToken_WhenFileDoesNotExist_ShouldReturnNull()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"non-existent-{Guid.NewGuid():N}.dat");
        var store = new DpapiTokenStore(nonExistentPath);

        var result = await store.LoadTokenAsync();

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ClearToken_ShouldDeletePersistedFile()
    {
        var store = new DpapiTokenStore(_tempFile);
        var token = new GitHubToken("ghp_test");
        await store.SaveTokenAsync(token);

        File.Exists(_tempFile).ShouldBeTrue();

        await store.ClearTokenAsync();

        File.Exists(_tempFile).ShouldBeFalse();
        (await store.LoadTokenAsync()).ShouldBeNull();
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            try { File.Delete(_tempFile); } catch { }
        }
    }
}
