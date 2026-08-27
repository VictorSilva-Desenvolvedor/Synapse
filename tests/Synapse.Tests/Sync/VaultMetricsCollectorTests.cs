using Shouldly;
using Synapse.Sync.Metrics;

namespace Synapse.Tests.Sync;

public class VaultMetricsCollectorTests : IDisposable
{
    private readonly string _tempVaultDir;

    public VaultMetricsCollectorTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-metrics-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
    }

    [Fact]
    public async Task CollectMetricsAsync_ShouldCountNotesWordsAndCategories()
    {
        var note1 = Path.Combine(_tempVaultDir, "Nota1.md");
        var note2 = Path.Combine(_tempVaultDir, "Subpasta", "Nota2.md");
        Directory.CreateDirectory(Path.GetDirectoryName(note2)!);

        await File.WriteAllTextAsync(note1, "---\ncategoria: Conceito\n---\n# Titulo\nEste é um texto com exatamente dez palavras no total agora.");
        await File.WriteAllTextAsync(note2, "---\ncategoria: Ideia\n---\n# Ideia\nMais cinco palavras aqui.");

        var metrics = await VaultMetricsCollector.CollectMetricsAsync(_tempVaultDir);

        metrics.TotalNotes.ShouldBe(2);
        metrics.TotalFolders.ShouldBe(1);
        metrics.TotalWords.ShouldBeGreaterThan(10);
        metrics.CategoryCounts.ContainsKey("Conceito").ShouldBeTrue();
        metrics.CategoryCounts.ContainsKey("Ideia").ShouldBeTrue();
        metrics.CategoryCounts["Conceito"].ShouldBe(1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }
}
