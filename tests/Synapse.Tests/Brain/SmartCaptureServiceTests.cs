using NSubstitute;
using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;

namespace Synapse.Tests.Brain;

public class SmartCaptureServiceTests : IDisposable
{
    private readonly string _tempVaultDir;

    public SmartCaptureServiceTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-brain-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
    }

    [Fact]
    public async Task ProcessAndSaveToVaultAsync_ShouldCreateStructuredMarkdownFileWithFrontmatter()
    {
        var mockProvider = Substitute.For<IBrainAiProvider>();
        mockProvider.ProcessRawNoteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiStructuredNote
            {
                Title = "Metodologia Zettelkasten",
                Category = "Conceito",
                Tags = ["pkm", "produtividade"],
                Summary = "Sistema de notas atômicas interligadas.",
                KeyPoints = ["Notas atômicas", "Princípio da ligação"],
                BodyMarkdown = "O Zettelkasten é uma metodologia poderosa de anotação.",
                SuggestedConnections = ["Segundo Cerebro"]
            }));

        var config = new BrainConfig
        {
            DefaultFolder = "Brain",
            AutoCategorizeFolders = true,
            EnableAutoLinking = true
        };

        var service = new SmartCaptureService(mockProvider, config);
        var rawInput = "Estudo sobre Zettelkasten e segundo cerebro";

        var relativePath = await service.ProcessAndSaveToVaultAsync(rawInput, _tempVaultDir);

        relativePath.ShouldBe("Brain/Conceito/Metodologia Zettelkasten.md");

        var fullPath = Path.Combine(_tempVaultDir, relativePath);
        File.Exists(fullPath).ShouldBeTrue();

        var content = await File.ReadAllTextAsync(fullPath);
        content.ShouldContain("---");
        content.ShouldContain("titulo: \"Metodologia Zettelkasten\"");
        content.ShouldContain("categoria: \"Conceito\"");
        content.ShouldContain("tags:");
        content.ShouldContain("- pkm");
        content.ShouldContain("# Metodologia Zettelkasten");
        content.ShouldContain("## Conexões & Notas Relacionadas");
        content.ShouldContain("- [[Segundo Cerebro]]");
    }

    [Fact]
    public async Task ProcessAndSaveToVaultAsync_WhenSameTitleExists_ShouldAppendNumericSuffix()
    {
        var mockProvider = Substitute.For<IBrainAiProvider>();
        mockProvider.ProcessRawNoteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiStructuredNote
            {
                Title = "Nota Duplicada",
                Category = "Ideia",
                BodyMarkdown = "Conteudo 1"
            }));

        var config = new BrainConfig { DefaultFolder = "Brain", AutoCategorizeFolders = false };
        var service = new SmartCaptureService(mockProvider, config);

        var first = await service.ProcessAndSaveToVaultAsync("Input 1", _tempVaultDir);
        var second = await service.ProcessAndSaveToVaultAsync("Input 2", _tempVaultDir);

        first.ShouldBe("Brain/Nota Duplicada.md");
        second.ShouldBe("Brain/Nota Duplicada (1).md");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }
}
