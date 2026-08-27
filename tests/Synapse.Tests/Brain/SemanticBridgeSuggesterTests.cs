using NSubstitute;
using Shouldly;
using Synapse.Brain.Graph;
using Synapse.Brain.Ports;

namespace Synapse.Tests.Brain;

public class SemanticBridgeSuggesterTests : IDisposable
{
    private readonly string _tempVaultDir;

    public SemanticBridgeSuggesterTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-bridge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
    }

    [Fact]
    public async Task FindBridgeSuggestionsAsync_WhenHighSimilarityAndNoLink_ShouldSuggestBridge()
    {
        var note1 = Path.Combine(_tempVaultDir, "DotNetCleanArch.md");
        var note2 = Path.Combine(_tempVaultDir, "RustPortsAdapters.md");

        await File.WriteAllTextAsync(note1, "Arquitetura limpa e desacoplada em C#.");
        await File.WriteAllTextAsync(note2, "Ports and Adapters implementado em Rust.");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        // Ambos recebem vetores quase idênticos
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0.9f, 0.8f }));

        var suggester = new SemanticBridgeSuggester(mockEmbedding);
        var suggestions = await suggester.FindBridgeSuggestionsAsync(_tempVaultDir, minSimilarity: 0.80f);

        suggestions.Count.ShouldBe(1);
        suggestions[0].NoteATitle.ShouldBeOneOf("DotNetCleanArch", "RustPortsAdapters");
        suggestions[0].SimilarityScore.ShouldBeGreaterThan(0.95f);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }
}
