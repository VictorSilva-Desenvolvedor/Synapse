using System.Threading.Channels;
using Shouldly;
using Synapse.Core.Ports;
using Synapse.Sync.Reconciliation;
using Synapse.Tests.TestDoubles;

namespace Synapse.Tests.Reconciliation;

public class ReconciliationJobTests : IDisposable
{
    private readonly string _tempVault;
    private readonly InMemorySyncIndexStore _indexStore = new();
    private readonly InMemoryFileSystem _fileSystem = new();
    private readonly Channel<VaultChangeEvent> _channel = Channel.CreateUnbounded<VaultChangeEvent>();

    public ReconciliationJobTests()
    {
        _tempVault = Path.Combine(Path.GetTempPath(), $"synapse-reconcile-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVault);
    }

    [Fact]
    public async Task ReconcileOnceAsync_WhenFileNotIndexed_ShouldEmitCreatedEvent()
    {
        // Arrange
        var filePath = Path.Combine(_tempVault, "new-note.md");
        File.WriteAllText(filePath, "# Brand New Note");
        await _fileSystem.WriteAllTextAsync(filePath, "# Brand New Note", CancellationToken.None);

        var job = new ReconciliationJob(_tempVault, _indexStore, _fileSystem, _channel.Writer);

        // Act
        var count = await job.ReconcileOnceAsync(CancellationToken.None);

        // Assert
        count.ShouldBe(1);
        _channel.Reader.TryRead(out var evt).ShouldBeTrue();
        evt!.RelativePath.ShouldBe("new-note.md");
        evt.EventType.ShouldBe(SyncEventType.Created);
    }

    [Fact]
    public async Task ReconcileOnceAsync_WhenFileHashChanged_ShouldEmitModifiedEvent()
    {
        // Arrange
        var filePath = Path.Combine(_tempVault, "existing-note.md");
        File.WriteAllText(filePath, "# Modified Content");
        await _fileSystem.WriteAllTextAsync(filePath, "# Modified Content", CancellationToken.None);

        var oldRecord = new SyncedFileRecord(
            Id: 1,
            LocalPath: "existing-note.md",
            CloudFileId: "sha-1",
            ContentHash: "old-sha-256-hash",
            LocalMtime: DateTimeOffset.UtcNow.AddHours(-1),
            CloudModifiedTime: DateTimeOffset.UtcNow.AddHours(-1),
            LastSyncedAt: DateTimeOffset.UtcNow.AddHours(-1),
            Status: SyncStatus.Synced);
        await _indexStore.UpsertAsync(oldRecord, CancellationToken.None);

        var job = new ReconciliationJob(_tempVault, _indexStore, _fileSystem, _channel.Writer);

        // Act
        var count = await job.ReconcileOnceAsync(CancellationToken.None);

        // Assert
        count.ShouldBe(1);
        _channel.Reader.TryRead(out var evt).ShouldBeTrue();
        evt!.RelativePath.ShouldBe("existing-note.md");
        evt.EventType.ShouldBe(SyncEventType.Modified);
    }

    [Fact]
    public async Task ReconcileOnceAsync_ShouldIgnoreSpecialDirectories()
    {
        // Arrange
        var gitDir = Path.Combine(_tempVault, ".git");
        var obsidianDir = Path.Combine(_tempVault, ".obsidian");
        var conflictDir = Path.Combine(_tempVault, "_conflitos");
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(obsidianDir);
        Directory.CreateDirectory(conflictDir);

        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main");
        File.WriteAllText(Path.Combine(obsidianDir, "workspace.json"), "{}");
        File.WriteAllText(Path.Combine(conflictDir, "conflito.md"), "diff");

        var job = new ReconciliationJob(_tempVault, _indexStore, _fileSystem, _channel.Writer);

        // Act
        var count = await job.ReconcileOnceAsync(CancellationToken.None);

        // Assert
        count.ShouldBe(0);
        _channel.Reader.TryRead(out _).ShouldBeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVault))
        {
            try { Directory.Delete(_tempVault, true); } catch { }
        }
    }
}
