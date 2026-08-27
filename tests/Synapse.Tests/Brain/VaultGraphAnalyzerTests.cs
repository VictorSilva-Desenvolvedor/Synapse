using Shouldly;
using Synapse.Brain.Graph;

namespace Synapse.Tests.Brain;

public class VaultGraphAnalyzerTests : IDisposable
{
    private readonly string _tempVaultDir;

    public VaultGraphAnalyzerTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-graph-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
    }

    [Fact]
    public async Task AnalyzeVaultAsync_ShouldIdentifyHubsAndDeadEnds()
    {
        // Hub central
        var noteHub = Path.Combine(_tempVaultDir, "SegundoCerebro.md");
        // Aponta para o hub
        var noteA = Path.Combine(_tempVaultDir, "Zettelkasten.md");
        var noteB = Path.Combine(_tempVaultDir, "Para.md");
        // Nota isolada
        var noteIso = Path.Combine(_tempVaultDir, "ListaMercado.md");

        await File.WriteAllTextAsync(noteHub, "# Segundo Cérebro\nVisão geral de PKM.");
        await File.WriteAllTextAsync(noteA, "# Zettelkasten\nConceito ligado ao [[SegundoCerebro]].");
        await File.WriteAllTextAsync(noteB, "# PARA\nMétodo de organização ligado ao [[SegundoCerebro]].");
        await File.WriteAllTextAsync(noteIso, "# Lista\nComprar maçã e banana.");

        var report = await VaultGraphAnalyzer.AnalyzeVaultAsync(_tempVaultDir);

        report.TotalNotes.ShouldBe(4);
        report.TotalLinks.ShouldBe(2);

        report.TopHubs.Count.ShouldBeGreaterThan(0);
        report.TopHubs[0].Title.ShouldBe("SegundoCerebro");
        report.TopHubs[0].InDegree.ShouldBe(2);

        report.IsolatedNotes.ShouldContain("ListaMercado");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }
}
