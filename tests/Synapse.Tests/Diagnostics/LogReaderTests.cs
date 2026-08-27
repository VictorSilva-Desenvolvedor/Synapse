using Shouldly;
using Synapse.Sync.Diagnostics;

namespace Synapse.Tests.Diagnostics;

public class LogReaderTests : IDisposable
{
    private readonly string _tempDir;

    public LogReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"synapse-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ReadTailLinesAsync_WhenFileDoesNotExist_ShouldReturnEmptyList()
    {
        var nonExistent = Path.Combine(_tempDir, "missing.log");
        var lines = await LogReader.ReadTailLinesAsync(nonExistent, 10);

        lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadTailLinesAsync_WhenFileHasFewerLines_ShouldReturnAllLines()
    {
        var logFile = Path.Combine(_tempDir, "synapse-test.log");
        var content = "Linha 1\nLinha 2\nLinha 3\n";
        await File.WriteAllTextAsync(logFile, content);

        var lines = await LogReader.ReadTailLinesAsync(logFile, 10);

        lines.Count.ShouldBe(3);
        lines[0].ShouldBe("Linha 1");
        lines[2].ShouldBe("Linha 3");
    }

    [Fact]
    public async Task ReadTailLinesAsync_WhenFileHasMoreLines_ShouldReturnLastNLines()
    {
        var logFile = Path.Combine(_tempDir, "synapse-test-large.log");
        var allLines = Enumerable.Range(1, 100).Select(i => $"Linha {i}").ToList();
        await File.WriteAllLinesAsync(logFile, allLines);

        var lines = await LogReader.ReadTailLinesAsync(logFile, 20);

        lines.Count.ShouldBe(20);
        lines[0].ShouldBe("Linha 81");
        lines[^1].ShouldBe("Linha 100");
    }

    [Fact]
    public async Task FindLatestLogFile_ShouldReturnMostRecentlyModifiedLogFile()
    {
        var oldFile = Path.Combine(_tempDir, "synapse-20260826.log");
        var newFile = Path.Combine(_tempDir, "synapse-20260827.log");

        await File.WriteAllTextAsync(oldFile, "log antigo");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddHours(-2));

        await File.WriteAllTextAsync(newFile, "log novo");
        File.SetLastWriteTimeUtc(newFile, DateTime.UtcNow);

        var latest = LogReader.FindLatestLogFile(_tempDir);

        latest.ShouldNotBeNull();
        Path.GetFileName(latest).ShouldBe("synapse-20260827.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
