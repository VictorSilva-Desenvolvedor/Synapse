using Shouldly;
using Synapse.Search;

namespace Synapse.Tests.Search;

public sealed class VaultIndexWatcherTests : IDisposable
{
    private readonly string _tempVaultDir;
    private readonly string _tempDbPath;
    private readonly SqliteVaultSearchIndex _searchIndex;
    private readonly VaultIndexWatcher _watcher;

    public VaultIndexWatcherTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-watcher-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);

        _tempDbPath = Path.Combine(Path.GetTempPath(), $"synapse-watcher-db-{Guid.NewGuid():N}.db");
        _searchIndex = SqliteVaultSearchIndex.ForFile(_tempDbPath);

        // 50ms de debounce para testes rápidos e responsivos
        _watcher = new VaultIndexWatcher(_searchIndex, debounceDelay: TimeSpan.FromMilliseconds(50));
        _watcher.Start(_tempVaultDir);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _searchIndex.Dispose();

        try
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
        }
        catch { }

        try
        {
            if (Directory.Exists(_tempVaultDir))
            {
                Directory.Delete(_tempVaultDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task FileCreated_AfterDebounce_IsIndexedInSearchIndex()
    {
        // Arrange & Act
        var note = Path.Combine(_tempVaultDir, "nova_nota.md");
        await File.WriteAllTextAsync(note, "Conteudo com termoCriadoWatcher para teste.");

        // Aguarda o debounce (50ms) + processamento I/O
        await Task.Delay(250);

        // Assert
        var results = new List<VaultSearchResult>();
        await foreach (var r in _searchIndex.SearchAsync("termoCriadoWatcher"))
        {
            results.Add(r);
        }

        results.Count.ShouldBe(1);
        results[0].FilePath.ShouldBe("nova_nota.md");
    }

    [Fact]
    public async Task FileModified_AfterDebounce_UpdatesContentInSearchIndex()
    {
        // Arrange
        var note = Path.Combine(_tempVaultDir, "nota_editavel.md");
        await File.WriteAllTextAsync(note, "Versao inicial com termoAntigoWatcher.");
        await Task.Delay(250);

        // Act - Edita o arquivo
        await File.WriteAllTextAsync(note, "Versao atualizada com termoNovoWatcher substituindo.");
        await Task.Delay(250);

        // Assert
        var resultsAntigo = new List<VaultSearchResult>();
        await foreach (var r in _searchIndex.SearchAsync("termoAntigoWatcher"))
        {
            resultsAntigo.Add(r);
        }
        resultsAntigo.ShouldBeEmpty();

        var resultsNovo = new List<VaultSearchResult>();
        await foreach (var r in _searchIndex.SearchAsync("termoNovoWatcher"))
        {
            resultsNovo.Add(r);
        }
        resultsNovo.Count.ShouldBe(1);
        resultsNovo[0].FilePath.ShouldBe("nota_editavel.md");
    }

    [Fact]
    public async Task FileDeleted_IsRemovedFromSearchIndex()
    {
        // Arrange
        var note = Path.Combine(_tempVaultDir, "nota_para_deletar.md");
        await File.WriteAllTextAsync(note, "Conteudo com termoDeletarWatcher.");
        await Task.Delay(250);

        (await _searchIndex.GetIndexedFileCountAsync()).ShouldBe(1);

        // Act
        File.Delete(note);
        await Task.Delay(250);

        // Assert
        (await _searchIndex.GetIndexedFileCountAsync()).ShouldBe(0);

        var results = new List<VaultSearchResult>();
        await foreach (var r in _searchIndex.SearchAsync("termoDeletarWatcher"))
        {
            results.Add(r);
        }
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task FileRenamed_RemovesOldPathAndIndexesNewPath()
    {
        // Arrange
        var oldFile = Path.Combine(_tempVaultDir, "antigo_nome.md");
        var newFile = Path.Combine(_tempVaultDir, "novo_nome.md");

        await File.WriteAllTextAsync(oldFile, "Conteudo com termoRenomearWatcher.");
        await Task.Delay(250);

        // Act
        File.Move(oldFile, newFile);
        await Task.Delay(250);

        // Assert
        var results = new List<VaultSearchResult>();
        await foreach (var r in _searchIndex.SearchAsync("termoRenomearWatcher"))
        {
            results.Add(r);
        }

        results.Count.ShouldBe(1);
        results[0].FilePath.ShouldBe("novo_nome.md");
    }

    [Fact]
    public async Task BurstOfSaves_TriggersOnlyOneReindexWithoutDuplication()
    {
        // Arrange
        var note = Path.Combine(_tempVaultDir, "burst.md");

        // Act - 10 escritas em rajada rápida no mesmo arquivo
        for (int i = 1; i <= 10; i++)
        {
            await File.WriteAllTextAsync(note, $"Conteudo com termoRajadaWatcher versao {i}");
            await Task.Delay(10);
        }

        // Aguarda estabilização do debounce após a última escrita
        await Task.Delay(300);

        // Assert
        (await _searchIndex.GetIndexedFileCountAsync()).ShouldBe(1);

        var results = new List<VaultSearchResult>();
        await foreach (var r in _searchIndex.SearchAsync("termoRajadaWatcher"))
        {
            results.Add(r);
        }

        results.Count.ShouldBe(1);
        results[0].FilePath.ShouldBe("burst.md");
        results[0].Snippet.ShouldContain("10"); // Deve ter indexado a versão final
    }

    [Fact]
    public async Task IgnoredDirectories_SuchAsObsidianAndTrashAndConflitos_AreNotIndexed()
    {
        // Arrange
        var obsidianDir = Path.Combine(_tempVaultDir, ".obsidian");
        var conflitosDir = Path.Combine(_tempVaultDir, "_conflitos");
        var trashDir = Path.Combine(_tempVaultDir, ".trash");

        Directory.CreateDirectory(obsidianDir);
        Directory.CreateDirectory(conflitosDir);
        Directory.CreateDirectory(trashDir);

        // Act
        await File.WriteAllTextAsync(Path.Combine(obsidianDir, "app.md"), "Configuracao em md no obsidian");
        await File.WriteAllTextAsync(Path.Combine(conflitosDir, "conflito.md"), "Copia de conflito");
        await File.WriteAllTextAsync(Path.Combine(trashDir, "lixo.md"), "Nota no lixo");

        await Task.Delay(250);

        // Assert
        (await _searchIndex.GetIndexedFileCountAsync()).ShouldBe(0);
    }

    [Fact]
    public void Stop_CancelsPendingDebouncesAndStopsWatcher()
    {
        _watcher.IsRunning.ShouldBeTrue();

        _watcher.Stop();

        _watcher.IsRunning.ShouldBeFalse();
    }
}
