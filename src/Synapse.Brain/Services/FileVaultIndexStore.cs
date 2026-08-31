using System.Security.Cryptography;
using System.Text;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;

namespace Synapse.Brain.Services;

/// <summary>
/// Implementação de persistência em disco do índice vetorial/lexical do cofre.
/// Armazena cache local em formato binário compacto em %LocalAppData%\Synapse\vault_index\<sha256>.idx.
/// </summary>
public sealed class FileVaultIndexStore : IVaultIndexStore
{
    private const uint MagicHeader = 0x494E5953; // "SYNI" em little-endian
    private const int FormatVersion = 1;

    private readonly string _baseDirectory;

    public FileVaultIndexStore(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Synapse",
            "vault_index");
    }

    public string GetIndexFilePath(string vaultRootPath)
    {
        var normalized = Path.GetFullPath(vaultRootPath).TrimEnd('\\', '/').ToLowerInvariant();
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Path.Combine(_baseDirectory, $"{hashHex}.idx");
    }

    public Task<Dictionary<string, NoteEmbeddingEntry>?> LoadAsync(string vaultRootPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath))
        {
            return Task.FromResult<Dictionary<string, NoteEmbeddingEntry>?>(null);
        }

        var filePath = GetIndexFilePath(vaultRootPath);
        if (!File.Exists(filePath))
        {
            return Task.FromResult<Dictionary<string, NoteEmbeddingEntry>?>(null);
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            var magic = reader.ReadUInt32();
            if (magic != MagicHeader)
            {
                return Task.FromResult<Dictionary<string, NoteEmbeddingEntry>?>(null);
            }

            var version = reader.ReadInt32();
            if (version != FormatVersion)
            {
                return Task.FromResult<Dictionary<string, NoteEmbeddingEntry>?>(null);
            }

            var count = reader.ReadInt32();
            var result = new Dictionary<string, NoteEmbeddingEntry>(count, StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < count; i++)
            {
                if (ct.IsCancellationRequested) return Task.FromResult<Dictionary<string, NoteEmbeddingEntry>?>(null);

                var relativePath = reader.ReadString();
                var contentHash = reader.ReadString();
                var updatedAtMs = reader.ReadInt64();
                var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(updatedAtMs);

                var vectorLength = reader.ReadInt32();
                var vector = new float[vectorLength];
                for (var v = 0; v < vectorLength; v++)
                {
                    vector[v] = reader.ReadSingle();
                }

                var tokensCount = reader.ReadInt32();
                var tokens = new List<string>(tokensCount);
                for (var t = 0; t < tokensCount; t++)
                {
                    tokens.Add(reader.ReadString());
                }

                result[relativePath] = new NoteEmbeddingEntry(relativePath, contentHash, vector, updatedAt, tokens);
            }

            return Task.FromResult<Dictionary<string, NoteEmbeddingEntry>?>(result);
        }
        catch
        {
            return Task.FromResult<Dictionary<string, NoteEmbeddingEntry>?>(null);
        }
    }

    public async Task SaveAsync(string vaultRootPath, IReadOnlyDictionary<string, NoteEmbeddingEntry> index, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath) || index == null)
        {
            return;
        }

        var filePath = GetIndexFilePath(vaultRootPath);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempFilePath = filePath + $".tmp-{Guid.NewGuid():N}";

        try
        {
            await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                writer.Write(MagicHeader);
                writer.Write(FormatVersion);
                writer.Write(index.Count);

                foreach (var (_, entry) in index)
                {
                    if (ct.IsCancellationRequested) return;

                    writer.Write(entry.RelativePath);
                    writer.Write(entry.ContentHash);
                    writer.Write(entry.UpdatedAt.ToUnixTimeMilliseconds());

                    writer.Write(entry.Vector.Length);
                    foreach (var val in entry.Vector)
                    {
                        writer.Write(val);
                    }

                    var tokens = entry.Tokens ?? [];
                    writer.Write(tokens.Count);
                    foreach (var token in tokens)
                    {
                        writer.Write(token);
                    }
                }
            }

            File.Move(tempFilePath, filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
        }
    }
}
