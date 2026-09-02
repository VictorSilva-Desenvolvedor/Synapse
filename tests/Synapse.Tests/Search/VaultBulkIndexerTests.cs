using Shouldly;
using Synapse.Search;

namespace Synapse.Tests.Search;

public sealed class VaultBulkIndexerTests : IDisposable
{
    private readonly string _tempVaultDir;
    private readonly string _tempDbPath;
    private readonly SqliteVaultSearchIndex _index;
    private readonly VaultBulkIndexer _indexer;

    public VaultBulkIndexerTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-bulk-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);

        _tempDbPath = Path.Combine(Path.GetTempPath(), $"synapse-bulk-test-db-{Guid.NewGuid():N}.db");
        _index = SqliteVaultSearchIndex.ForFile(_tempDbPath);
        _indexer = new VaultBulkIndexer(batchSize: 5);
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
            // Ignored on cleanup
        }

        try
        {
            if (Directory.Exists(_tempVaultDir))
            {
                Directory.Delete(_tempVaultDir, recursive: true);
            }
        }
        catch
        {
            // Ignored on cleanup
        }
    }

    [Fact]
    public async Task IndexVaultAsync_IndexesAllMarkdownFilesAndIgnoresNonMdFiles()
    {
        // Arrange
        var note1 = Path.Combine(_tempVaultDir, "note1.md");
        await File.WriteAllTextAsync(note1, "Content in root markdown file.");

        var subDir = Path.Combine(_tempVaultDir, "subfolder");
        Directory.CreateDirectory(subDir);
        var note2 = Path.Combine(subDir, "note2.md");
        await File.WriteAllTextAsync(note2, "Content in subfolder markdown file.");

        var deepDir = Path.Combine(subDir, "deep");
        Directory.CreateDirectory(deepDir);
        var note3 = Path.Combine(deepDir, "note3.md");
        await File.WriteAllTextAsync(note3, "Content in deep markdown file.");

        // Non-markdown files that MUST be ignored
        var txtFile = Path.Combine(_tempVaultDir, "notes.txt");
        await File.WriteAllTextAsync(txtFile, "Content in txt file that should be ignored.");

        var jsonFile = Path.Combine(subDir, "data.json");
        await File.WriteAllTextAsync(jsonFile, "{\"title\": \"json content\"}");

        var pngFile = Path.Combine(deepDir, "image.png");
        await File.WriteAllBytesAsync(pngFile, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        // Act
        var indexedCount = await _indexer.IndexVaultAsync(_tempVaultDir, _index);

        // Assert
        indexedCount.ShouldBe(3);
        (await _index.GetIndexedFileCountAsync()).ShouldBe(3);

        // Verify markdown content is searchable
        var resultsRoot = new List<VaultSearchResult>();
        await foreach (var r in _index.SearchAsync("root"))
        {
            resultsRoot.Add(r);
        }
        resultsRoot.Count.ShouldBe(1);
        resultsRoot[0].FilePath.ShouldEndWith("note1.md");

        var resultsDeep = new List<VaultSearchResult>();
        await foreach (var r in _index.SearchAsync("deep"))
        {
            resultsDeep.Add(r);
        }
        resultsDeep.Count.ShouldBe(1);
        resultsDeep[0].FilePath.ShouldEndWith("note3.md");

        // Verify non-markdown content was NOT indexed
        var resultsTxt = new List<VaultSearchResult>();
        await foreach (var r in _index.SearchAsync("ignored"))
        {
            resultsTxt.Add(r);
        }
        resultsTxt.ShouldBeEmpty();
    }

    [Fact]
    public async Task IndexVaultAsync_ReportsProgressViaIProgress()
    {
        // Arrange
        int totalFiles = 12;
        for (int i = 1; i <= totalFiles; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_tempVaultDir, $"doc_{i:D2}.md"),
                $"Document number {i} content.");
        }

        var progressReports = new List<int>();
        var progress = new Progress<int>(count => progressReports.Add(count));

        // Act - batchSize is 5, so reports at 5, 10, 12
        var indexedCount = await _indexer.IndexVaultAsync(_tempVaultDir, _index, progress);

        // Assert
        indexedCount.ShouldBe(totalFiles);
        (await _index.GetIndexedFileCountAsync()).ShouldBe(totalFiles);

        progressReports.ShouldNotBeEmpty();
        progressReports.Last().ShouldBe(totalFiles);
    }

    [Fact]
    public async Task IndexVaultAsync_WhenCancelled_AbortsAndThrowsOperationCanceledException()
    {
        // Arrange
        for (int i = 0; i < 20; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_tempVaultDir, $"file_{i}.md"),
                $"File content {i}");
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await _indexer.IndexVaultAsync(_tempVaultDir, _index, ct: cts.Token);
        });
    }

    [Fact]
    public async Task IndexVaultAsync_WhenVaultDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        var nonExistent = Path.Combine(_tempVaultDir, "non_existent_vault_404");

        await Should.ThrowAsync<DirectoryNotFoundException>(async () =>
        {
            await _indexer.IndexVaultAsync(nonExistent, _index);
        });
    }

    [Fact]
    public async Task IndexVaultAsync_WithNullOrWhitespaceVaultRoot_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await _indexer.IndexVaultAsync("   ", _index);
        });
    }
}
