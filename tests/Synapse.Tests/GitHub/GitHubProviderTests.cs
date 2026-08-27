using System.Net;
using System.Text.Json;
using Shouldly;
using Synapse.Core.Ports;
using Synapse.Sync.Auth;
using Synapse.Sync.GitHub;

namespace Synapse.Tests.GitHub;

public class GitHubProviderTests : IDisposable
{
    private readonly InMemoryTokenStore _tokenStore = new();
    private readonly GitHubClientConfig _config = new()
    {
        Owner = "VictorSilva-Desenvolvedor",
        Repository = "Synapse-Vault",
        Branch = "main"
    };
    private readonly GitHubAuthManager _authManager;
    private readonly string _tempFile;

    public GitHubProviderTests()
    {
        _authManager = new GitHubAuthManager(_tokenStore, _config);
        _tokenStore.SaveTokenAsync(new GitHubToken("ghp_test_token")).Wait();

        _tempFile = Path.Combine(Path.GetTempPath(), $"synapse-ghtest-{Guid.NewGuid():N}.md");
        File.WriteAllText(_tempFile, "# Hello GitHub Synapse");
    }

    [Fact]
    public async Task UploadAsync_WhenLocalFileDoesNotExist_ShouldThrowFileNotFoundException()
    {
        using var httpClient = new HttpClient();
        using var provider = new GitHubProvider(_authManager, _config, httpClient);

        await Should.ThrowAsync<FileNotFoundException>(async () =>
        {
            await provider.UploadAsync("c:\\caminho\\inexistente.md", string.Empty, CancellationToken.None);
        });
    }

    [Fact]
    public async Task UploadAsync_WhenFileExists_ShouldSendBase64PutRequestAndReturnCloudFile()
    {
        var responsePayload = new
        {
            content = new
            {
                name = Path.GetFileName(_tempFile),
                path = $"notes/{Path.GetFileName(_tempFile)}",
                sha = "blob-sha-12345"
            }
        };

        var handler = new MockHttpMessageHandler(async (req) =>
        {
            req.Method.ShouldBe(HttpMethod.Put);
            req.RequestUri!.ToString().ShouldContain("/contents/notes/");
            var body = await req.Content!.ReadAsStringAsync();
            body.ShouldContain("Sync: criar");
            body.ShouldContain("content");

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(JsonSerializer.Serialize(responsePayload))
            };
        });

        using var httpClient = new HttpClient(handler);
        using var provider = new GitHubProvider(_authManager, _config, httpClient);

        var result = await provider.UploadAsync(_tempFile, "notes", CancellationToken.None);

        result.ShouldNotBeNull();
        result.Id.ShouldBe($"notes/{Path.GetFileName(_tempFile)}");
        result.Name.ShouldBe(Path.GetFileName(_tempFile));
        result.Md5Checksum.ShouldBe("blob-sha-12345");
        result.Trashed.ShouldBeFalse();
    }

    [Fact]
    public async Task DownloadAsync_ShouldDownloadRawContentAtomically()
    {
        var destFile = Path.Combine(Path.GetTempPath(), $"downloaded-{Guid.NewGuid():N}.md");

        var handler = new MockHttpMessageHandler((req) =>
        {
            req.Method.ShouldBe(HttpMethod.Get);
            req.RequestUri!.ToString().ShouldContain("/contents/notes/test.md");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("# Downloaded from GitHub")
            });
        });

        using var httpClient = new HttpClient(handler);
        using var provider = new GitHubProvider(_authManager, _config, httpClient);

        await provider.DownloadAsync("notes/test.md", destFile, CancellationToken.None);

        File.Exists(destFile).ShouldBeTrue();
        File.ReadAllText(destFile).ShouldBe("# Downloaded from GitHub");

        File.Delete(destFile);
    }

    [Fact]
    public async Task GetStartPageTokenAsync_ShouldReturnLatestCommitSha()
    {
        var commitPayload = new { sha = "6dcb09b5b57875f334f61aebed695e2e4193db5e" };

        var handler = new MockHttpMessageHandler((req) =>
        {
            req.RequestUri!.ToString().ShouldContain("/commits/main");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(commitPayload))
            });
        });

        using var httpClient = new HttpClient(handler);
        using var provider = new GitHubProvider(_authManager, _config, httpClient);

        var token = await provider.GetStartPageTokenAsync(CancellationToken.None);

        token.ShouldBe("6dcb09b5b57875f334f61aebed695e2e4193db5e");
    }

    [Fact]
    public async Task GetChangesAsync_WhenShaIsSame_ShouldReturnNoChanges()
    {
        var commitPayload = new { sha = "same-commit-sha" };

        var handler = new MockHttpMessageHandler((req) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(commitPayload))
            });
        });

        using var httpClient = new HttpClient(handler);
        using var provider = new GitHubProvider(_authManager, _config, httpClient);

        var changes = await provider.GetChangesAsync("same-commit-sha", CancellationToken.None);

        changes.ChangedFiles.ShouldBeEmpty();
        changes.NewStartPageToken.ShouldBe("same-commit-sha");
    }

    [Fact]
    public async Task GetChangesAsync_WhenShaDiffers_ShouldCallCompareAndReturnFiles()
    {
        var commitPayload = new { sha = "head-sha-789" };
        var comparePayload = new
        {
            files = new[]
            {
                new { filename = "Diario/2026-08-27.md", status = "modified", sha = "blob-sha-1" },
                new { filename = "Inbox/ideia.md", status = "added", sha = "blob-sha-2" },
                new { filename = "Velho/removido.md", status = "removed", sha = "blob-sha-3" }
            }
        };

        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("/commits/main"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(commitPayload))
                });
            }

            req.RequestUri!.ToString().ShouldContain("/compare/base-sha-123...head-sha-789");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(comparePayload))
            });
        });

        using var httpClient = new HttpClient(handler);
        using var provider = new GitHubProvider(_authManager, _config, httpClient);

        var changes = await provider.GetChangesAsync("base-sha-123", CancellationToken.None);

        changes.ChangedFiles.Count.ShouldBe(3);
        changes.ChangedFiles[0].Id.ShouldBe("Diario/2026-08-27.md");
        changes.ChangedFiles[0].Trashed.ShouldBeFalse();

        changes.ChangedFiles[1].Id.ShouldBe("Inbox/ideia.md");
        changes.ChangedFiles[1].Trashed.ShouldBeFalse();

        changes.ChangedFiles[2].Id.ShouldBe("Velho/removido.md");
        changes.ChangedFiles[2].Trashed.ShouldBeTrue();

        changes.NewStartPageToken.ShouldBe("head-sha-789");
    }

    [Fact]
    public async Task EnsureRepositoryAsync_WhenRepoNotFound_ShouldCreatePrivateRepo()
    {
        var createdPayload = new { id = 12345, name = "Synapse-Vault" };
        var repoCreated = false;

        var handler = new MockHttpMessageHandler(async (req) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains("/repos/VictorSilva-Desenvolvedor/Synapse-Vault"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/user/repos"))
            {
                var body = await req.Content!.ReadAsStringAsync();
                body.ShouldContain("\"private\":true");
                repoCreated = true;

                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(JsonSerializer.Serialize(createdPayload))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var httpClient = new HttpClient(handler);
        using var provider = new GitHubProvider(_authManager, _config, httpClient);

        await provider.EnsureRepositoryAsync(CancellationToken.None);

        repoCreated.ShouldBeTrue();
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            try { File.Delete(_tempFile); } catch { }
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
