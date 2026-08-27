using Shouldly;
using Synapse.Sync.Snapshots;

namespace Synapse.Tests.Sync;

public class SnapshotManagerTests : IDisposable
{
    private readonly string _tempVaultDir;

    public SnapshotManagerTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-snapshot-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
    }

    [Fact]
    public async Task SaveSnapshotAsync_ShouldSaveRevisionAndAllowRestoration()
    {
        var noteRelPath = "Notas/Design.md";
        var noteFullPath = Path.Combine(_tempVaultDir, noteRelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(noteFullPath)!);

        var originalContent = "# Design V1\nConceitos iniciais.";
        var modifiedContent = "# Design V2\nConceitos modificados.";

        await File.WriteAllTextAsync(noteFullPath, originalContent);

        // 1. Salva snapshot da V1
        var snapshotPath = await SnapshotManager.SaveSnapshotAsync(_tempVaultDir, noteRelPath, originalContent);
        snapshotPath.ShouldNotBeNull();
        File.Exists(snapshotPath).ShouldBeTrue();

        // 2. Modifica arquivo para V2
        await File.WriteAllTextAsync(noteFullPath, modifiedContent);

        // 3. Obtém snapshots
        var list = SnapshotManager.GetSnapshots(_tempVaultDir, noteRelPath);
        list.Count.ShouldBe(1);

        // 4. Restaura snapshot V1
        var restored = await SnapshotManager.RestoreSnapshotAsync(_tempVaultDir, noteRelPath, list[0].FullSnapshotPath);
        restored.ShouldBeTrue();

        var currentContent = await File.ReadAllTextAsync(noteFullPath);
        currentContent.ShouldBe(originalContent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }
}
