using System.Security.Cryptography;
using Shouldly;
using Synapse.Sync.Backup;

namespace Synapse.Tests.Sync;

public class VaultBackupExporterTests : IDisposable
{
    private readonly string _sourceVaultDir;
    private readonly string _targetVaultDir;
    private readonly string _backupFilePath;

    public VaultBackupExporterTests()
    {
        var basePath = Path.Combine(Path.GetTempPath(), $"synapse-backup-test-{Guid.NewGuid():N}");
        _sourceVaultDir = Path.Combine(basePath, "SourceVault");
        _targetVaultDir = Path.Combine(basePath, "TargetVault");
        _backupFilePath = Path.Combine(basePath, "vault-backup.synapse-backup");

        Directory.CreateDirectory(_sourceVaultDir);
        Directory.CreateDirectory(_targetVaultDir);
    }

    [Fact]
    public async Task ExportAndImportEncryptedBackup_ShouldPreserveFilesAndVerifyIntegrity()
    {
        var note1 = Path.Combine(_sourceVaultDir, "Nota1.md");
        var note2 = Path.Combine(_sourceVaultDir, "Pasta", "Nota2.md");
        Directory.CreateDirectory(Path.GetDirectoryName(note2)!);

        await File.WriteAllTextAsync(note1, "# Nota 1\nConteudo da nota 1.");
        await File.WriteAllTextAsync(note2, "# Nota 2\nConteudo da nota 2.");

        const string password = "SenhaSuperSecreta123!";

        // 1. Exporta backup criptografado
        await VaultBackupExporter.ExportEncryptedBackupAsync(_sourceVaultDir, _backupFilePath, password);
        File.Exists(_backupFilePath).ShouldBeTrue();

        // 2. Importa no cofre alvo
        await VaultBackupExporter.ImportEncryptedBackupAsync(_backupFilePath, _targetVaultDir, password);

        // 3. Valida conteúdo restaurado
        var restored1 = Path.Combine(_targetVaultDir, "Nota1.md");
        var restored2 = Path.Combine(_targetVaultDir, "Pasta", "Nota2.md");

        File.Exists(restored1).ShouldBeTrue();
        File.Exists(restored2).ShouldBeTrue();

        var text1 = await File.ReadAllTextAsync(restored1);
        text1.ShouldBe("# Nota 1\nConteudo da nota 1.");
    }

    [Fact]
    public async Task ImportEncryptedBackup_WithWrongPassword_ShouldThrowCryptographicException()
    {
        var note1 = Path.Combine(_sourceVaultDir, "Nota1.md");
        await File.WriteAllTextAsync(note1, "# Nota 1");

        await VaultBackupExporter.ExportEncryptedBackupAsync(_sourceVaultDir, _backupFilePath, "SenhaCorreta");

        await Should.ThrowAsync<CryptographicException>(async () =>
        {
            await VaultBackupExporter.ImportEncryptedBackupAsync(_backupFilePath, _targetVaultDir, "SenhaIncorreta");
        });
    }

    public void Dispose()
    {
        var baseDir = Path.GetDirectoryName(_sourceVaultDir);
        if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
        {
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }
}
