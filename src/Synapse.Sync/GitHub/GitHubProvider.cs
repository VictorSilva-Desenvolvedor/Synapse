using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Synapse.Core.Ports;
using File = System.IO.File;

namespace Synapse.Sync.GitHub;

/// <summary>
/// Implementação concreta de ICloudProvider para a GitHub REST API v3 (RNF-6, RF-SYNC.2, ADR-003, ADR-017).
/// Sincroniza notas diretamente com um repositório privado no GitHub.
/// </summary>
public sealed class GitHubProvider : ICloudProvider, IDisposable
{
    private readonly GitHubAuthManager _authManager;
    private readonly GitHubClientConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubProvider>? _logger;

    public GitHubProvider(
        GitHubAuthManager authManager,
        GitHubClientConfig config,
        HttpClient? httpClient = null,
        ILogger<GitHubProvider>? logger = null)
    {
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger;
    }

    public async Task<CloudFile> UploadAsync(string localPath, string remoteFolderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        if (!File.Exists(localPath))
        {
            throw new FileNotFoundException($"Arquivo local não encontrado para upload: {localPath}", localPath);
        }

        var fileName = Path.GetFileName(localPath);
        var relativePath = string.IsNullOrWhiteSpace(remoteFolderId)
            ? fileName
            : $"{remoteFolderId.TrimEnd('/', '\\')}/{fileName}".Replace('\\', '/');

        var bytes = await File.ReadAllBytesAsync(localPath, ct);
        var base64Content = Convert.ToBase64String(bytes);

        var payload = new
        {
            message = $"Sync: criar {relativePath}",
            content = base64Content,
            branch = _config.Branch
        };

        var json = JsonSerializer.Serialize(payload);
        var url = BuildContentsUrl(relativePath);

        try
        {
            using var response = await SendAuthorizedRequestAsync(HttpMethod.Put, url, new StringContent(json, Encoding.UTF8, "application/json"), ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw GitHubExceptionMapper.Map(response, errorBody);
            }

            var resBody = await response.Content.ReadAsStringAsync(ct);
            var contentRes = JsonSerializer.Deserialize<GitHubContentResponse>(resBody);

            _logger?.LogInformation("Upload concluído para o GitHub: {Path} (SHA: {Sha})", relativePath, contentRes?.Content?.Sha);

            return new CloudFile(
                Id: contentRes?.Content?.Path ?? relativePath,
                Name: contentRes?.Content?.Name ?? fileName,
                Md5Checksum: contentRes?.Content?.Sha ?? string.Empty,
                ModifiedTime: DateTimeOffset.UtcNow,
                Trashed: false);
        }
        catch (Exception ex) when (ex is not CloudAuthExpiredException and not CloudQuotaExceededException and not CloudNotFoundException and not CloudTransientException)
        {
            throw GitHubExceptionMapper.Map(ex);
        }
    }

    public async Task<CloudFile> UpdateAsync(string cloudFileId, string localPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudFileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        if (!File.Exists(localPath))
        {
            throw new FileNotFoundException($"Arquivo local não encontrado para atualização: {localPath}", localPath);
        }

        var normalizedPath = cloudFileId.Replace('\\', '/').TrimStart('/');
        var existingFile = await GetContentInternalAsync(normalizedPath, ct);
        var sha = existingFile?.Sha;

        var bytes = await File.ReadAllBytesAsync(localPath, ct);
        var base64Content = Convert.ToBase64String(bytes);

        var payload = new
        {
            message = $"Sync: atualizar {normalizedPath}",
            content = base64Content,
            sha = sha,
            branch = _config.Branch
        };

        var json = JsonSerializer.Serialize(payload);
        var url = BuildContentsUrl(normalizedPath);

        try
        {
            using var response = await SendAuthorizedRequestAsync(HttpMethod.Put, url, new StringContent(json, Encoding.UTF8, "application/json"), ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw GitHubExceptionMapper.Map(response, errorBody);
            }

            var resBody = await response.Content.ReadAsStringAsync(ct);
            var contentRes = JsonSerializer.Deserialize<GitHubContentResponse>(resBody);

            _logger?.LogInformation("Atualização concluída no GitHub: {Path} (SHA: {Sha})", normalizedPath, contentRes?.Content?.Sha);

            return new CloudFile(
                Id: contentRes?.Content?.Path ?? normalizedPath,
                Name: contentRes?.Content?.Name ?? Path.GetFileName(normalizedPath),
                Md5Checksum: contentRes?.Content?.Sha ?? string.Empty,
                ModifiedTime: DateTimeOffset.UtcNow,
                Trashed: false);
        }
        catch (Exception ex) when (ex is not CloudAuthExpiredException and not CloudQuotaExceededException and not CloudNotFoundException and not CloudTransientException)
        {
            throw GitHubExceptionMapper.Map(ex);
        }
    }

    public async Task DownloadAsync(string cloudFileId, string destinationPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudFileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var normalizedPath = cloudFileId.Replace('\\', '/').TrimStart('/');
        var url = BuildContentsUrl(normalizedPath);

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            // Usando cabeçalho raw para download direto do conteúdo
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
            var token = await _authManager.GetValidTokenAsync(ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd(_config.UserAgent);

            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw GitHubExceptionMapper.Map(response, errorBody);
            }

            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fileStream, ct);
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            _logger?.LogInformation("Download concluído do GitHub para {Path}", destinationPath);
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }

            if (ex is CloudAuthExpiredException or CloudQuotaExceededException or CloudNotFoundException or CloudTransientException)
            {
                throw;
            }

            throw GitHubExceptionMapper.Map(ex);
        }
    }

    public async Task DeleteAsync(string cloudFileId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudFileId);

        var normalizedPath = cloudFileId.Replace('\\', '/').TrimStart('/');
        var existingFile = await GetContentInternalAsync(normalizedPath, ct);
        if (existingFile == null || string.IsNullOrEmpty(existingFile.Sha))
        {
            _logger?.LogWarning("Arquivo não encontrado no GitHub para exclusão: {Path}", normalizedPath);
            return;
        }

        var payload = new
        {
            message = $"Sync: remover {normalizedPath}",
            sha = existingFile.Sha,
            branch = _config.Branch
        };

        var json = JsonSerializer.Serialize(payload);
        var url = BuildContentsUrl(normalizedPath);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var token = await _authManager.GetValidTokenAsync(ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd(_config.UserAgent);

            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw GitHubExceptionMapper.Map(response, errorBody);
            }

            _logger?.LogInformation("Arquivo removido do repositório GitHub: {Path}", normalizedPath);
        }
        catch (Exception ex) when (ex is not CloudAuthExpiredException and not CloudQuotaExceededException and not CloudNotFoundException and not CloudTransientException)
        {
            throw GitHubExceptionMapper.Map(ex);
        }
    }

    public async Task<CloudFile> GetMetadataAsync(string cloudFileId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudFileId);

        var normalizedPath = cloudFileId.Replace('\\', '/').TrimStart('/');
        var content = await GetContentInternalAsync(normalizedPath, ct);

        if (content == null)
        {
            throw new CloudNotFoundException($"Arquivo {normalizedPath} não encontrado no repositório GitHub.");
        }

        return new CloudFile(
            Id: content.Path ?? normalizedPath,
            Name: content.Name ?? Path.GetFileName(normalizedPath),
            Md5Checksum: content.Sha ?? string.Empty,
            ModifiedTime: DateTimeOffset.UtcNow,
            Trashed: false);
    }

    public async Task<string> GetStartPageTokenAsync(CancellationToken ct)
    {
        var url = $"{_config.BaseUrl.TrimEnd('/')}/repos/{_config.Owner}/{_config.Repository}/commits/{_config.Branch}";

        try
        {
            using var response = await SendAuthorizedRequestAsync(HttpMethod.Get, url, null, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw GitHubExceptionMapper.Map(response, errorBody);
            }

            var resBody = await response.Content.ReadAsStringAsync(ct);
            var commit = JsonSerializer.Deserialize<GitHubCommitResponse>(resBody);

            return commit?.Sha ?? string.Empty;
        }
        catch (Exception ex) when (ex is not CloudAuthExpiredException and not CloudQuotaExceededException and not CloudNotFoundException and not CloudTransientException)
        {
            throw GitHubExceptionMapper.Map(ex);
        }
    }

    public async Task<ChangesPage> GetChangesAsync(string pageToken, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageToken);

        var headSha = await GetStartPageTokenAsync(ct);

        if (string.Equals(pageToken, headSha, StringComparison.OrdinalIgnoreCase))
        {
            return new ChangesPage(
                ChangedFiles: Array.Empty<CloudFile>(),
                NextPageToken: null,
                NewStartPageToken: headSha);
        }

        var compareUrl = $"{_config.BaseUrl.TrimEnd('/')}/repos/{_config.Owner}/{_config.Repository}/compare/{pageToken}...{headSha}";

        try
        {
            using var response = await SendAuthorizedRequestAsync(HttpMethod.Get, compareUrl, null, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw GitHubExceptionMapper.Map(response, errorBody);
            }

            var resBody = await response.Content.ReadAsStringAsync(ct);
            var compareRes = JsonSerializer.Deserialize<GitHubCompareResponse>(resBody);

            var changedFiles = new List<CloudFile>();
            if (compareRes?.Files != null)
            {
                foreach (var f in compareRes.Files)
                {
                    var isRemoved = string.Equals(f.Status, "removed", StringComparison.OrdinalIgnoreCase);
                    changedFiles.Add(new CloudFile(
                        Id: f.Filename ?? string.Empty,
                        Name: Path.GetFileName(f.Filename ?? string.Empty),
                        Md5Checksum: f.Sha ?? string.Empty,
                        ModifiedTime: DateTimeOffset.UtcNow,
                        Trashed: isRemoved));
                }
            }

            return new ChangesPage(
                ChangedFiles: changedFiles,
                NextPageToken: null,
                NewStartPageToken: headSha);
        }
        catch (Exception ex) when (ex is not CloudAuthExpiredException and not CloudQuotaExceededException and not CloudNotFoundException and not CloudTransientException)
        {
            throw GitHubExceptionMapper.Map(ex);
        }
    }

    /// <summary>
    /// Garante que o repositório privado existe no GitHub (RF-AUTH.2).
    /// </summary>
    public async Task EnsureRepositoryAsync(CancellationToken ct = default)
    {
        var repoUrl = $"{_config.BaseUrl.TrimEnd('/')}/repos/{_config.Owner}/{_config.Repository}";
        using var checkResp = await SendAuthorizedRequestAsync(HttpMethod.Get, repoUrl, null, ct);

        if (checkResp.IsSuccessStatusCode)
        {
            _logger?.LogInformation("Repositório GitHub já existe: {Owner}/{Repo}", _config.Owner, _config.Repository);
            return;
        }

        if (checkResp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger?.LogInformation("Criando repositório privado no GitHub: {Repo}", _config.Repository);
            var createUrl = $"{_config.BaseUrl.TrimEnd('/')}/user/repos";
            var payload = new
            {
                name = _config.Repository,
                @private = true,
                auto_init = true,
                description = "Synapse Vault - Sincronização Obsidian"
            };

            var json = JsonSerializer.Serialize(payload);
            using var createResp = await SendAuthorizedRequestAsync(HttpMethod.Post, createUrl, new StringContent(json, Encoding.UTF8, "application/json"), ct);

            if (!createResp.IsSuccessStatusCode)
            {
                var err = await createResp.Content.ReadAsStringAsync(ct);
                throw GitHubExceptionMapper.Map(createResp, err);
            }

            _logger?.LogInformation("Repositório privado criado com sucesso no GitHub: {Owner}/{Repo}", _config.Owner, _config.Repository);
        }
        else
        {
            var err = await checkResp.Content.ReadAsStringAsync(ct);
            throw GitHubExceptionMapper.Map(checkResp, err);
        }
    }

    private async Task<GitHubContentItem?> GetContentInternalAsync(string path, CancellationToken ct)
    {
        var url = BuildContentsUrl(path);
        using var response = await SendAuthorizedRequestAsync(HttpMethod.Get, url, null, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw GitHubExceptionMapper.Map(response, errorBody);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<GitHubContentItem>(json);
    }

    private async Task<HttpResponseMessage> SendAuthorizedRequestAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = content
        };

        var token = await _authManager.GetValidTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd(_config.UserAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");

        return await _httpClient.SendAsync(request, ct);
    }

    private string BuildContentsUrl(string path)
    {
        var cleanPath = path.Replace('\\', '/').TrimStart('/');
        return $"{_config.BaseUrl.TrimEnd('/')}/repos/{_config.Owner}/{_config.Repository}/contents/{cleanPath}?ref={_config.Branch}";
    }

    public void Dispose()
    {
    }

    private sealed class GitHubContentResponse
    {
        [JsonPropertyName("content")]
        public GitHubContentItem? Content { get; set; }

        [JsonPropertyName("commit")]
        public GitHubCommitItem? Commit { get; set; }
    }

    private sealed class GitHubContentItem
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("sha")]
        public string? Sha { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    private sealed class GitHubCommitItem
    {
        [JsonPropertyName("sha")]
        public string? Sha { get; set; }
    }

    private sealed class GitHubCommitResponse
    {
        [JsonPropertyName("sha")]
        public string? Sha { get; set; }
    }

    private sealed class GitHubCompareResponse
    {
        [JsonPropertyName("files")]
        public List<GitHubCompareFile>? Files { get; set; }
    }

    private sealed class GitHubCompareFile
    {
        [JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("sha")]
        public string? Sha { get; set; }
    }
}
