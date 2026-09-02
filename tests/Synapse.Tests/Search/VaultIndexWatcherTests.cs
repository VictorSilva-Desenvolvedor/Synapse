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

    /// <summary>
    /// Aguarda o indice refletir o resultado esperado, em vez de dormir um tempo fixo.
    /// Um Task.Delay calibrado na maquina de desenvolvimento nao cobre o runner do CI, que roda a
    /// suite inteira em paralelo em dois nucleos - foi assim que FileRenamed quebrou o pipeline
    /// esperando 250ms por um evento do FileSystemWatcher que la demorava mais. Com polling o teste
    /// continua rapido localmente (sai assim que a condicao bate) e tolerante sob carga.
    /// </summary>
    private Task<List<VaultSearchResult>> AguardarResultados(string termo, int esperado, int timeoutMs = 15000) =>
        AguardarCondicao(termo, r => r.Count == esperado, timeoutMs);

    private async Task<List<VaultSearchResult>> AguardarCondicao(
        string termo,
        Func<List<VaultSearchResult>, bool> condicao,
        int timeoutMs = 15000)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var encontrados = new List<VaultSearchResult>();

        while (true)
        {
            encontrados.Clear();
            await foreach (var r in _searchIndex.SearchAsync(termo))
            {
                encontrados.Add(r);
            }

            if (condicao(encontrados) || DateTime.UtcNow >= limite)
            {
                return encontrados;
            }

            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task FileCreated_AfterDebounce_IsIndexedInSearchIndex()
    {
        // Arrange & Act
        var note = Path.Combine(_tempVaultDir, "nova_nota.md");
        await File.WriteAllTextAsync(note, "Conteudo com termoCriadoWatcher para teste.");

        // Assert
        var results = await AguardarResultados("termoCriadoWatcher", esperado: 1);

        results.Count.ShouldBe(1);
        results[0].FilePath.ShouldBe("nova_nota.md");
    }

    [Fact]
    public async Task FileModified_AfterDebounce_UpdatesContentInSearchIndex()
    {
        // Arrange
        var note = Path.Combine(_tempVaultDir, "nota_editavel.md");
        await File.WriteAllTextAsync(note, "Versao inicial com termoAntigoWatcher.");
        await AguardarResultados("termoAntigoWatcher", esperado: 1);

        // Act - Edita o arquivo
        await File.WriteAllTextAsync(note, "Versao atualizada com termoNovoWatcher substituindo.");

        // Assert
        var resultsNovo = await AguardarResultados("termoNovoWatcher", esperado: 1);
        var resultsAntigo = await AguardarResultados("termoAntigoWatcher", esperado: 0);

        resultsAntigo.ShouldBeEmpty();
        resultsNovo.Count.ShouldBe(1);
        resultsNovo[0].FilePath.ShouldBe("nota_editavel.md");
    }

    [Fact]
    public async Task FileDeleted_IsRemovedFromSearchIndex()
    {
        // Arrange
        var note = Path.Combine(_tempVaultDir, "nota_para_deletar.md");
        await File.WriteAllTextAsync(note, "Conteudo com termoDeletarWatcher.");
        (await AguardarResultados("termoDeletarWatcher", esperado: 1)).Count.ShouldBe(1);

        // Act
        File.Delete(note);

        // Assert
        var results = await AguardarResultados("termoDeletarWatcher", esperado: 0);
        results.ShouldBeEmpty();
        (await _searchIndex.GetIndexedFileCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task FileRenamed_RemovesOldPathAndIndexesNewPath()
    {
        // Arrange
        var oldFile = Path.Combine(_tempVaultDir, "antigo_nome.md");
        var newFile = Path.Combine(_tempVaultDir, "novo_nome.md");

        await File.WriteAllTextAsync(oldFile, "Conteudo com termoRenomearWatcher.");
        await AguardarResultados("termoRenomearWatcher", esperado: 1);

        // Act
        File.Move(oldFile, newFile);

        // Assert - aguarda o caminho NOVO especificamente. Esperar so "1 resultado" retornaria de
        // imediato, porque o caminho antigo ainda conta 1 ate o watcher processar a renomeacao.
        var results = await AguardarCondicao(
            "termoRenomearWatcher",
            r => r.Count == 1 && r[0].FilePath == "novo_nome.md");

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
