using NSubstitute;
using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;

namespace Synapse.Tests.Brain;

public class KnowledgeDigestServiceTests : IDisposable
{
    private readonly string _tempVaultDir;

    public KnowledgeDigestServiceTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-digest-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
    }

    [Fact]
    public async Task GenerateWeeklyDigestAsync_ShouldGenerateWeeklyDigestFile()
    {
        var note1 = Path.Combine(_tempVaultDir, "Estudo CSharp.md");
        var note2 = Path.Combine(_tempVaultDir, "Nota Orfa.md");

        await File.WriteAllTextAsync(note1, "# C# 12\nEstudando novas features com [[DotNet]].");
        await File.WriteAllTextAsync(note2, "# Ideia Isolada\nSem nenhum link.");

        var mockAi = Substitute.For<IBrainAiProvider>();
        mockAi.ProcessRawNoteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiStructuredNote
            {
                Title = "Síntese Semanal",
                BodyMarkdown = "Esta semana focamos em estudos de linguagem e novas ideias."
            }));

        var config = new BrainConfig { DefaultFolder = "Brain" };
        var digestService = new KnowledgeDigestService(mockAi, config);

        var relativePath = await digestService.GenerateWeeklyDigestAsync(_tempVaultDir, DateTimeOffset.UtcNow);

        relativePath.ShouldStartWith("Brain/Digests/Digest-");
        var fullPath = Path.Combine(_tempVaultDir, relativePath);
        File.Exists(fullPath).ShouldBeTrue();

        var content = await File.ReadAllTextAsync(fullPath);
        content.ShouldContain("## 📌 Notas Trabalhadas no Período");
        content.ShouldContain("- [[Estudo CSharp]]");
        content.ShouldContain("## 💡 Notas Órfãs (Oportunidades de Conexão)");
        content.ShouldContain("- [[Nota Orfa]]");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }
}
