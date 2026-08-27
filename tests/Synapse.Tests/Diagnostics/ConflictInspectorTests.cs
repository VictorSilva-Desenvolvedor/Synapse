using Shouldly;
using Synapse.Sync.Diagnostics;

namespace Synapse.Tests.Diagnostics;

public class ConflictInspectorTests : IDisposable
{
    private readonly string _tempVaultDir;

    public ConflictInspectorTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-vault-conflicts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
    }

    [Fact]
    public async Task ListConflictsAsync_WhenNoConflictsDir_ShouldReturnEmptyList()
    {
        var conflicts = await ConflictInspector.ListConflictsAsync(_tempVaultDir);

        conflicts.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListConflictsAsync_WhenConflictsExist_ShouldReturnPopulatedConflictItems()
    {
        var conflictsDir = Path.Combine(_tempVaultDir, "_conflitos");
        Directory.CreateDirectory(conflictsDir);

        var conflict1 = Path.Combine(conflictsDir, "Nota1.conflito-20260827.md");
        var conflict2 = Path.Combine(conflictsDir, "Sub", "Nota2.conflito-20260827.md");
        Directory.CreateDirectory(Path.GetDirectoryName(conflict2)!);

        await File.WriteAllTextAsync(conflict1, "conteudo 1");
        await File.WriteAllTextAsync(conflict2, "conteudo 2");

        var list = await ConflictInspector.ListConflictsAsync(_tempVaultDir);

        list.Count.ShouldBe(2);
        list.Any(c => c.FileName == "Nota1.conflito-20260827.md").ShouldBeTrue();
        list.Any(c => c.FileName == "Nota2.conflito-20260827.md").ShouldBeTrue();
        list.All(c => c.RelativePath.StartsWith("_conflitos/")).ShouldBeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }
}
