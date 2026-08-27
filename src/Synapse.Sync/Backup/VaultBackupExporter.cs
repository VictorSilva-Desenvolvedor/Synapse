using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Synapse.Sync.Crypto;

namespace Synapse.Sync.Backup;

/// <summary>
/// Exportador e Importador de Backups Criptografados do Cofre com Verificação SHA-512 (V8.2, US-SEC.1).
/// </summary>
public static class VaultBackupExporter
{
    private static readonly byte[] MagicHeader = Encoding.ASCII.GetBytes("SYNAPSE_BACKUP_V1\n");

    public static async Task ExportEncryptedBackupAsync(
        string vaultRootPath,
        string destinationBackupFilePath,
        string password,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationBackupFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (!Directory.Exists(vaultRootPath))
        {
            throw new DirectoryNotFoundException($"Cofre não encontrado em: {vaultRootPath}");
        }

        // 1. Cria arquivo zip em memória
        using var zipMemory = new MemoryStream();
        using (var archive = new ZipArchive(zipMemory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var files = Directory.GetFiles(vaultRootPath, "*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(".synapse") && !f.Contains(".obsidian/workspace") && !f.Contains(".trash"))
                .ToList();

            foreach (var file in files)
            {
                var relPath = Path.GetRelativePath(vaultRootPath, file).Replace('\\', '/');
                var entry = archive.CreateEntry(relPath, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream, ct);
            }
        }

        var zipBytes = zipMemory.ToArray();

        // 2. Calcula Checksum SHA-512 do zip antes de criptografar
        var sha512Checksum = SHA512.HashData(zipBytes);

        // 3. Criptografa o zip com AES-256-GCM
        var salt = VaultCrypto.GenerateSalt();
        var key = VaultCrypto.DeriveKey(password, salt);
        var encryptedBytes = VaultCrypto.Encrypt(zipBytes, key, salt);

        // 4. Grava arquivo de backup com Header + Checksum + Criptografia
        using var outputStream = File.Create(destinationBackupFilePath);
        await outputStream.WriteAsync(MagicHeader, ct);
        await outputStream.WriteAsync(sha512Checksum, ct); // 64 bytes
        await outputStream.WriteAsync(encryptedBytes, ct);
    }

    public static async Task ImportEncryptedBackupAsync(
        string backupFilePath,
        string targetVaultRootPath,
        string password,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVaultRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException("Arquivo de backup não encontrado.", backupFilePath);
        }

        var backupBytes = await File.ReadAllBytesAsync(backupFilePath, ct);

        // 1. Valida Magic Header
        if (backupBytes.Length < MagicHeader.Length + 64)
        {
            throw new InvalidDataException("Formato de backup inválido ou corrompido.");
        }

        for (var i = 0; i < MagicHeader.Length; i++)
        {
            if (backupBytes[i] != MagicHeader[i])
            {
                throw new InvalidDataException("Cabeçalho de backup incompatível.");
            }
        }

        // 2. Extrai Checksum esperado e Payload Criptografado
        var expectedSha512 = new byte[64];
        Array.Copy(backupBytes, MagicHeader.Length, expectedSha512, 0, 64);

        var payloadOffset = MagicHeader.Length + 64;
        var encryptedPayload = new byte[backupBytes.Length - payloadOffset];
        Array.Copy(backupBytes, payloadOffset, encryptedPayload, 0, encryptedPayload.Length);

        // 3. Extrai salt do payload criptografado (está após o HeaderMagic de 14 bytes)
        var salt = new byte[VaultCrypto.SaltSizeBytes];
        Array.Copy(encryptedPayload, 14, salt, 0, VaultCrypto.SaltSizeBytes);
        var key = VaultCrypto.DeriveKey(password, salt);

        // 4. Descriptografa payload
        var decryptedZipBytes = VaultCrypto.Decrypt(encryptedPayload, key);

        // 5. Valida integridade SHA-512
        var actualSha512 = SHA512.HashData(decryptedZipBytes);
        if (!actualSha512.SequenceEqual(expectedSha512))
        {
            throw new CryptographicException("Falha na verificação de integridade do backup (SHA-512 divergente).");
        }

        // 6. Descompacta no diretório alvo
        Directory.CreateDirectory(targetVaultRootPath);
        using var zipMemory = new MemoryStream(decryptedZipBytes);
        using var archive = new ZipArchive(zipMemory, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.Combine(targetVaultRootPath, entry.FullName);
            var destDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            using var entryStream = entry.Open();
            using var fileStream = File.Create(destinationPath);
            await entryStream.CopyToAsync(fileStream, ct);
        }
    }
}
