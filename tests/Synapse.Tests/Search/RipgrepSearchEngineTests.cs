using System.Diagnostics;
using Shouldly;
using Synapse.Search;

namespace Synapse.Tests.Search;

public sealed class RipgrepSearchEngineTests : IDisposable
{
    private readonly string _tempVaultDir;
    private readonly RipgrepSearchEngine _engine;

    public RipgrepSearchEngineTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-search-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
        _engine = new RipgrepSearchEngine();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempVaultDir))
            {
                Directory.Delete(_tempVaultDir, recursive: true);
            }
        }
        catch
        {
            // Ignored on test cleanup
        }
    }

    [Fact]
    public async Task SearchAsync_WithLiteralMatches_ReturnsExpectedMatches()
    {
        // Arrange
        var note1 = Path.Combine(_tempVaultDir, "note1.md");
        await File.WriteAllLinesAsync(note1, new[]
        {
            "# Document Title",
            "First line with uniqueKeyWord in it.",
            "Second line with another content.",
            "Third line with uniqueKeyWord again at the end."
        });

        var subDir = Path.Combine(_tempVaultDir, "sub");
        Directory.CreateDirectory(subDir);
        var note2 = Path.Combine(subDir, "note2.md");
        await File.WriteAllLinesAsync(note2, new[]
        {
            "Different file with no matches."
        });

        // Act
        var matches = new List<RipgrepMatch>();
        await foreach (var match in _engine.SearchAsync(_tempVaultDir, "uniqueKeyWord", isRegex: false))
        {
            matches.Add(match);
        }

        // Assert
        matches.Count.ShouldBe(2);

        matches[0].FilePath.ShouldEndWith("note1.md");
        matches[0].LineNumber.ShouldBe(2);
        matches[0].LineText.ShouldBe("First line with uniqueKeyWord in it.");
        matches[0].MatchStart.ShouldBe(16);
        matches[0].MatchEnd.ShouldBe(29);

        matches[1].FilePath.ShouldEndWith("note1.md");
        matches[1].LineNumber.ShouldBe(4);
        matches[1].LineText.ShouldBe("Third line with uniqueKeyWord again at the end.");
        matches[1].MatchStart.ShouldBe(16);
        matches[1].MatchEnd.ShouldBe(29);
    }

    [Fact]
    public async Task SearchAsync_WithSpecialCharacters_WhenNotRegex_TreatsAsLiteral()
    {
        // Arrange
        var note = Path.Combine(_tempVaultDir, "special.md");
        await File.WriteAllLinesAsync(note, new[]
        {
            "Line with [C#] (v8.0) + *special* $100.00 characters.",
            "Another line without special pattern."
        });

        var literalPattern = "[C#] (v8.0) + *special* $100.00";

        // Act
        var matches = new List<RipgrepMatch>();
        await foreach (var match in _engine.SearchAsync(_tempVaultDir, literalPattern, isRegex: false))
        {
            matches.Add(match);
        }

        // Assert
        matches.Count.ShouldBe(1);
        matches[0].LineNumber.ShouldBe(1);
        matches[0].LineText.ShouldBe("Line with [C#] (v8.0) + *special* $100.00 characters.");
    }

    [Fact]
    public async Task SearchAsync_WithRegex_MatchesPatternCorrectly()
    {
        // Arrange
        var note = Path.Combine(_tempVaultDir, "regex.md");
        await File.WriteAllLinesAsync(note, new[]
        {
            "Order #12345 confirmed",
            "Random text with nothing",
            "Invoice #98765 paid",
            "Ticket #00000 ignored"
        });

        var regexPattern = @"(Order|Invoice) #\d{5}";

        // Act
        var matches = new List<RipgrepMatch>();
        await foreach (var match in _engine.SearchAsync(_tempVaultDir, regexPattern, isRegex: true))
        {
            matches.Add(match);
        }

        // Assert
        matches.Count.ShouldBe(2);

        matches[0].LineNumber.ShouldBe(1);
        matches[0].LineText.ShouldBe("Order #12345 confirmed");
        matches[0].MatchStart.ShouldBe(0);
        matches[0].MatchEnd.ShouldBe(12);

        matches[1].LineNumber.ShouldBe(3);
        matches[1].LineText.ShouldBe("Invoice #98765 paid");
        matches[1].MatchStart.ShouldBe(0);
        matches[1].MatchEnd.ShouldBe(14);
    }

    [Fact]
    public async Task SearchAsync_WhenNoMatchesFound_ReturnsEmptyEnumerableWithoutThrowing()
    {
        // Arrange
        var note = Path.Combine(_tempVaultDir, "note.md");
        await File.WriteAllTextAsync(note, "Some markdown content here.");

        // Act
        var matches = new List<RipgrepMatch>();
        await foreach (var match in _engine.SearchAsync(_tempVaultDir, "NON_EXISTENT_STRING_XYZ_9999", isRegex: false))
        {
            matches.Add(match);
        }

        // Assert
        matches.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WhenCancelled_CancelsQuicklyAndLeavesNoOrphanProcess()
    {
        // Arrange
        for (int i = 0; i < 50; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_tempVaultDir, $"file_{i}.md"),
                $"Line {i} content repeated: " + string.Join(" ", Enumerable.Range(0, 100).Select(x => $"word_{x}")));
        }

        int spawnedPid = 0;
        _engine.OnProcessStarted = p =>
        {
            try
            {
                spawnedPid = p.Id;
            }
            catch { }
        };

        using var cts = new CancellationTokenSource();

        // Act & Assert - cancel during iteration
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _engine.SearchAsync(_tempVaultDir, "word", isRegex: false, ct: cts.Token))
            {
                cts.Cancel();
            }
        });

        // Verify the specific spawned process was terminated and did not become an orphan
        spawnedPid.ShouldBeGreaterThan(0);

        bool isProcessAlive;
        try
        {
            using var p = Process.GetProcessById(spawnedPid);
            isProcessAlive = !p.HasExited;
        }
        catch (ArgumentException)
        {
            // Process with this PID has completely exited
            isProcessAlive = false;
        }

        isProcessAlive.ShouldBeFalse();
    }

    [Fact]
    public async Task SearchAsync_WithInvalidRegex_ThrowsInvalidOperationException()
    {
        // Arrange
        var note = Path.Combine(_tempVaultDir, "note.md");
        await File.WriteAllTextAsync(note, "Sample content");

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in _engine.SearchAsync(_tempVaultDir, @"(?<unclosed", isRegex: true))
            {
            }
        });

        ex.Message.ShouldContain("Ripgrep process exited with code");
    }

    [Fact]
    public async Task SearchAsync_WithNonExistentDirectory_ThrowsDirectoryNotFoundException()
    {
        var nonExistentDir = Path.Combine(_tempVaultDir, "non_existent_dir_404");

        await Should.ThrowAsync<DirectoryNotFoundException>(async () =>
        {
            await foreach (var _ in _engine.SearchAsync(nonExistentDir, "pattern"))
            {
            }
        });
    }

    [Fact]
    public async Task SearchAsync_WithNullOrWhitespaceVaultRoot_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in _engine.SearchAsync("   ", "pattern"))
            {
            }
        });
    }
}
