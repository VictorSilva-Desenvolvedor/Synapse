using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Services;

namespace Synapse.Tests.Brain;

public class FileVaultIndexStoreTests : IDisposable
{
    private readonly string _tempIndexDir;

    public FileVaultIndexStoreTests()
    {
        _tempIndexDir = Path.Combine(Path.GetTempPath(), $"synapse-idx-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempIndexDir);
    }

    [Fact]
    public async Task SaveAsync_And_LoadAsync_ShouldPersistAndRestoreCompleteIndex()
    {
        var store = new FileVaultIndexStore(_tempIndexDir);
        var vaultPath = "C:\\Vaults\\MeuCofre";

        var index = new Dictionary<string, NoteEmbeddingEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["Conceitos/Arquitetura.md"] = new NoteEmbeddingEntry(
                "Conceitos/Arquitetura.md",
                "hash-123456",
                [0.1f, 0.2f, 0.3f, 0.4f],
                DateTimeOffset.UtcNow,
                ["arquitetura", "hexagonal", "ports", "adapters"]),
            ["Tarefas/Sprint.md"] = new NoteEmbeddingEntry(
                "Tarefas/Sprint.md",
                "hash-789012",
                [0.9f, 0.8f, 0.7f, 0.6f],
                DateTimeOffset.UtcNow,
                ["tarefas", "sprint", "planejamento"])
        };

        await store.SaveAsync(vaultPath, index);

        var loaded = await store.LoadAsync(vaultPath);

        loaded.ShouldNotBeNull();
        loaded.Count.ShouldBe(2);

        var entry1 = loaded["Conceitos/Arquitetura.md"];
        entry1.RelativePath.ShouldBe("Conceitos/Arquitetura.md");
        entry1.ContentHash.ShouldBe("hash-123456");
        entry1.Vector.ShouldBe(new float[] { 0.1f, 0.2f, 0.3f, 0.4f });
        entry1.Tokens.ShouldContain("arquitetura");
        entry1.Tokens.ShouldContain("adapters");

        var entry2 = loaded["Tarefas/Sprint.md"];
        entry2.RelativePath.ShouldBe("Tarefas/Sprint.md");
        entry2.ContentHash.ShouldBe("hash-789012");
        entry2.Tokens.ShouldContain("sprint");
    }

    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ShouldReturnNullGracefully()
    {
        var store = new FileVaultIndexStore(_tempIndexDir);
        var loaded = await store.LoadAsync("C:\\Vaults\\Inexistente");
        loaded.ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_WhenFileCorrupted_ShouldReturnNullGracefully()
    {
        var store = new FileVaultIndexStore(_tempIndexDir);
        var vaultPath = "C:\\Vaults\\Corrompido";
        var filePath = store.GetIndexFilePath(vaultPath);

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "dados invalidos que nao sao binario SYNI");

        var loaded = await store.LoadAsync(vaultPath);
        loaded.ShouldBeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempIndexDir))
        {
            try { Directory.Delete(_tempIndexDir, true); } catch { }
        }
    }
}
