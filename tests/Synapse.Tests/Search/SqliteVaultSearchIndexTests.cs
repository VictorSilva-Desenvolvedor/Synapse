using Shouldly;
using Synapse.Search;

namespace Synapse.Tests.Search;

public sealed class SqliteVaultSearchIndexTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteVaultSearchIndex _index;

    public SqliteVaultSearchIndexTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"synapse-vault-index-test-{Guid.NewGuid():N}.db");
        _index = SqliteVaultSearchIndex.ForFile(_tempDbPath);
    }

    public void Dispose()
    {
        _index.Dispose();
        try
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
        }
        catch
        {
            // Ignored on test cleanup
        }
    }

    [Fact]
    public async Task SearchAsync_WhenTermIndexed_ReturnsMatchingResultWithSnippetAndRank()
    {
        // Arrange
        await _index.IndexFileAsync("notes/architecture.md", "This document covers the Synapse hybrid search engine architecture.");
        await _index.IndexFileAsync("notes/recipes.md", "Chocolate cake recipe with sugar and flour.");

        // Act
        var results = new List<VaultSearchResult>();
        await foreach (var item in _index.SearchAsync("architecture"))
        {
            results.Add(item);
        }

        // Assert
        results.Count.ShouldBe(1);
        results[0].FilePath.ShouldBe("notes/architecture.md");
        results[0].Snippet.ShouldContain("<b>architecture</b>");
        results[0].Rank.ShouldBeLessThan(0.0); // BM25 negative score
    }

    [Fact]
    public async Task SearchAsync_WhenQueryWithoutDiacritics_MatchesAccentedContent()
    {
        // Arrange
        await _index.IndexFileAsync("notas/padroes.md", "Aqui temos o padrão de código e sincronização avançada.");

        // Act - search without diacritics
        var resultsCodigo = new List<VaultSearchResult>();
        await foreach (var item in _index.SearchAsync("codigo"))
        {
            resultsCodigo.Add(item);
        }

        var resultsSincronizacao = new List<VaultSearchResult>();
        await foreach (var item in _index.SearchAsync("sincronizacao"))
        {
            resultsSincronizacao.Add(item);
        }

        var resultsPadrao = new List<VaultSearchResult>();
        await foreach (var item in _index.SearchAsync("padrao"))
        {
            resultsPadrao.Add(item);
        }

        // Assert
        resultsCodigo.Count.ShouldBe(1);
        resultsCodigo[0].FilePath.ShouldBe("notas/padroes.md");

        resultsSincronizacao.Count.ShouldBe(1);
        resultsSincronizacao[0].FilePath.ShouldBe("notas/padroes.md");

        resultsPadrao.Count.ShouldBe(1);
        resultsPadrao[0].FilePath.ShouldBe("notas/padroes.md");
    }

    [Fact]
    public async Task IndexFileAsync_WhenReindexingSameFile_UpdatesContentWithoutDuplicating()
    {
        // Arrange
        await _index.IndexFileAsync("notes/doc.md", "Initial version with alpha keyword.");
        var countInitial = await _index.GetIndexedFileCountAsync();
        countInitial.ShouldBe(1);

        // Act - Reindex with new content
        await _index.IndexFileAsync("notes/doc.md", "Updated version with beta keyword instead.");
        var countAfterReindex = await _index.GetIndexedFileCountAsync();

        var resultsOld = new List<VaultSearchResult>();
        await foreach (var r in _index.SearchAsync("alpha"))
        {
            resultsOld.Add(r);
        }

        var resultsNew = new List<VaultSearchResult>();
        await foreach (var r in _index.SearchAsync("beta"))
        {
            resultsNew.Add(r);
        }

        // Assert
        countAfterReindex.ShouldBe(1);
        resultsOld.ShouldBeEmpty();
        resultsNew.Count.ShouldBe(1);
        resultsNew[0].FilePath.ShouldBe("notes/doc.md");
    }

    [Fact]
    public async Task RemoveFileAsync_RemovesFileFromIndex()
    {
        // Arrange
        await _index.IndexFileAsync("notes/to-delete.md", "Content to be deleted.");
        var countBefore = await _index.GetIndexedFileCountAsync();
        countBefore.ShouldBe(1);

        // Act
        await _index.RemoveFileAsync("notes/to-delete.md");
        var countAfter = await _index.GetIndexedFileCountAsync();

        var results = new List<VaultSearchResult>();
        await foreach (var r in _index.SearchAsync("deleted"))
        {
            results.Add(r);
        }

        // Assert
        countAfter.ShouldBe(0);
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WhenNoMatches_ReturnsEmptyWithoutThrowing()
    {
        // Arrange
        await _index.IndexFileAsync("notes/test.md", "Some plain content.");

        // Act
        var results = new List<VaultSearchResult>();
        await foreach (var r in _index.SearchAsync("nonexistent_unique_term_9999"))
        {
            results.Add(r);
        }

        // Assert
        results.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("AND")]
    [InlineData("OR")]
    [InlineData("NOT")]
    [InlineData("C#")]
    [InlineData("NEAR(word1")]
    public async Task SearchAsync_WithFts5SpecialCharacters_FindsLiteralTokensWithoutOperatorInterpretation(string term)
    {
        // Arrange
        await _index.IndexFileAsync("notes/specials.md", "Line containing C# .NET AND OR NOT symbols NEAR(word1 word2).");
        await _index.IndexFileAsync("notes/other.md", "Plain line without those reserved words.");

        // Act
        var results = new List<VaultSearchResult>();
        await foreach (var r in _index.SearchAsync(term))
        {
            results.Add(r);
        }

        // Assert: encontra exatamente o arquivo com o termo literal e não confunde com operador FTS5
        results.Count.ShouldBe(1);
        results[0].FilePath.ShouldBe("notes/specials.md");
    }

    [Fact]
    public async Task GetIndexedFileCountAsync_ReturnsAccurateCount()
    {
        // Arrange
        (await _index.GetIndexedFileCountAsync()).ShouldBe(0);

        var batch = new List<(string FilePath, string Content)>
        {
            ("file1.md", "content 1"),
            ("file2.md", "content 2"),
            ("file3.md", "content 3")
        };

        // Act
        await _index.IndexBatchAsync(batch);

        // Assert
        (await _index.GetIndexedFileCountAsync()).ShouldBe(3);
    }
}
