namespace Synapse.Core.Ports;

/// <summary>
/// Abstrai o provedor de nuvem (RNF-6). Implementação da v1: GoogleDriveProvider em Synapse.Sync.
/// Pré-condições: localPath deve existir e ser legível no momento da chamada; cloudFileId deve ser
/// um ID previamente retornado por este mesmo provider.
/// </summary>
public interface ICloudProvider
{
    /// <summary>
    /// Pós-condição: o CloudFile retornado reflete o estado confirmado no Drive, não uma estimativa local.
    /// </summary>
    Task<CloudFile> UploadAsync(string localPath, string remoteFolderId, CancellationToken ct);

    /// <summary>
    /// Pós-condição: o CloudFile retornado reflete o estado confirmado no Drive, não uma estimativa local.
    /// </summary>
    Task<CloudFile> UpdateAsync(string cloudFileId, string localPath, CancellationToken ct);

    Task DownloadAsync(string cloudFileId, string destinationPath, CancellationToken ct);

    Task DeleteAsync(string cloudFileId, CancellationToken ct);

    Task<CloudFile> GetMetadataAsync(string cloudFileId, CancellationToken ct);

    Task<string> GetStartPageTokenAsync(CancellationToken ct);

    Task<ChangesPage> GetChangesAsync(string pageToken, CancellationToken ct);
}

public sealed record CloudFile(
    string Id,
    string Name,
    string Md5Checksum,
    DateTimeOffset ModifiedTime,
    bool Trashed);

public sealed record ChangesPage(
    IReadOnlyList<CloudFile> ChangedFiles,
    string? NextPageToken,
    string? NewStartPageToken);
