using NSubstitute;
using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;

namespace Synapse.Tests.Brain;

public class VaultRagEngineTests : IDisposable
{
    private readonly string _tempVaultDir;

    public VaultRagEngineTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-rag-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
    }

    [Fact]
    public async Task SearchAsync_ShouldRankNotesBySimilarityScore()
    {
        var note1Path = Path.Combine(_tempVaultDir, "Zettelkasten.md");
        var note2Path = Path.Combine(_tempVaultDir, "Receitas.md");

        await File.WriteAllTextAsync(note1Path, "Anotações atômicas e PKM com Zettelkasten.");
        await File.WriteAllTextAsync(note2Path, "Receita de bolo de chocolate e café.");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        // Vetor de consulta
        mockEmbedding.GenerateEmbeddingAsync("segundo cerebro", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0.9f, 0f }));

        // Vetor da nota 1 (alta similaridade)
        mockEmbedding.GenerateEmbeddingAsync(Arg.Is<string>(s => s.Contains("Zettelkasten")), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.95f, 0.85f, 0f }));

        // Vetor da nota 2 (baixa similaridade)
        mockEmbedding.GenerateEmbeddingAsync(Arg.Is<string>(s => s.Contains("Receita")), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0f, 0.1f, 0.9f }));

        var mockAi = Substitute.For<IBrainAiProvider>();

        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);
        var results = await ragEngine.SearchAsync("segundo cerebro", _tempVaultDir, topK: 2);

        results.Count.ShouldBe(2);
        results[0].Title.ShouldBe("Zettelkasten");
        results[0].SimilarityScore.ShouldBeGreaterThan(results[1].SimilarityScore);
    }

    [Fact]
    public async Task AskVaultAsync_ShouldAugmentPromptWithTopNotesAndReturnAnswer()
    {
        var notePath = Path.Combine(_tempVaultDir, "Arquitetura.md");
        await File.WriteAllTextAsync(notePath, "O Synapse adota a Arquitetura Hexagonal (Ports and Adapters).");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 1f, 1f }));

        var mockAi = Substitute.For<IBrainAiProvider>();
        mockAi.ProcessRawNoteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiStructuredNote
            {
                Title = "Resposta RAG",
                BodyMarkdown = "O projeto utiliza a [[Arquitetura]] Hexagonal.",
                Summary = "O projeto utiliza a [[Arquitetura]] Hexagonal."
            }));

        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);
        var answer = await ragEngine.AskVaultAsync("Qual é o padrão arquitetural do Synapse?", _tempVaultDir);

        answer.Answer.ShouldContain("Hexagonal");
        answer.Sources.Count.ShouldBeGreaterThan(0);
        answer.Sources[0].Title.ShouldBe("Arquitetura");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }
}
