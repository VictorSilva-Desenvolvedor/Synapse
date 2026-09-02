using NSubstitute;
using Shouldly;
using Synapse.Search;

namespace Synapse.Tests.Search;

public sealed class HybridSearchServiceTests : IDisposable
{
    private readonly string _tempVaultDir;
    private readonly string _tempDbPath;
    private readonly HybridSearchService _service;

    public HybridSearchServiceTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-service-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);

        _tempDbPath = Path.Combine(Path.GetTempPath(), $"synapse-service-db-{Guid.NewGuid():N}.db");
        _service = HybridSearchService.ForVault(
            databaseFilePath: _tempDbPath,
            watcherDebounce: TimeSpan.FromMilliseconds(50),
            bulkBatchSize: 10);
    }

    public void Dispose()
    {
        _service.Dispose();

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

    private async Task<List<HybridSearchResult>> SearchAsync(string query)
    {
        var list = new List<HybridSearchResult>();
        await foreach (var item in _service.SearchAsync(query))
        {
            list.Add(item);
        }
        return list;
    }

    [Fact]
    public async Task InitializeAsync_RunsBulkIndexAndStartsWatcher()
    {
        // Arrange
        for (int i = 1; i <= 8; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_tempVaultDir, $"doc_{i}.md"),
                $"Documento {i} com termoInicializacaoService no cofre.");
        }

        int progressReported = 0;
        var progress = new SynchronousProgress<int>(p => progressReported = p);

        // Act
        await _service.InitializeAsync(_tempVaultDir, progress);

        // Assert
        _service.IsInitialized.ShouldBeTrue();
        _service.IsBulkIndexing.ShouldBeFalse();
        _service.IsIndexReady.ShouldBeTrue();
        progressReported.ShouldBe(8);

        var results = await SearchAsync("termoInicializacaoService");
        results.Count.ShouldBe(8);
        results.All(r => r.Source == SearchMatchSource.IndexOnly).ShouldBeTrue();
    }

    [Fact]
    public async Task SearchAsync_DuringBulkIndexInProgress_ReturnsResultsWithoutBlockingOrThrowing()
    {
        // Arrange
        for (int i = 1; i <= 50; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_tempVaultDir, $"bulk_{i}.md"),
                $"Documento {i} com termoConcorrenteBulk para teste.");
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Act - Dispara inicialização/bulk index em background
        var initTask = _service.InitializeAsync(_tempVaultDir, ct: cts.Token);

        // Dispara buscas concorrentes imediatamente enquanto o bulk ainda pode estar rodando
        var searchTask = Task.Run(async () =>
        {
            var totalSearches = 0;
            for (int i = 0; i < 15; i++)
            {
                var results = new List<HybridSearchResult>();
                try
                {
                    await foreach (var r in _service.SearchAsync("termoConcorrenteBulk", ct: cts.Token))
                    {
                        results.Add(r);
                    }
                    totalSearches++;
                }
                catch (InvalidOperationException)
                {
                    // Se a busca rodar antes do InitializeAsync setar o _vaultRootPath
                    await Task.Delay(20, cts.Token);
                }
            }

            return totalSearches;
        }, cts.Token);

        await Task.WhenAll(initTask, searchTask);

        // Assert
        (await searchTask).ShouldBeGreaterThan(0);
        _service.IsInitialized.ShouldBeTrue();

        var finalResults = await SearchAsync("termoConcorrenteBulk");
        finalResults.Count.ShouldBe(50);
    }

    [Fact]
    public async Task WatcherIndexingAndConcurrentSearches_RunSimultaneouslyWithoutDeadlockOrExceptions()
    {
        // Arrange
        var baseFile = Path.Combine(_tempVaultDir, "ancora.md");
        await File.WriteAllTextAsync(baseFile, "Nota ancora com termoEstresseWatcher.");

        await _service.InitializeAsync(_tempVaultDir);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Background: mutações contínuas de arquivos no disco que acionam o watcher
        var mutationsTask = Task.Run(async () =>
        {
            for (int i = 0; i < 15; i++)
            {
                var path = Path.Combine(_tempVaultDir, $"dinamico_{i}.md");
                await File.WriteAllTextAsync(path, $"Texto dinamico {i} com termoEstresseWatcher", cts.Token);
                await Task.Delay(15, cts.Token);
            }
        }, cts.Token);

        // Foreground: 20 buscas híbridas concorrentes
        var searchesTask = Task.Run(async () =>
        {
            int foundAncoraCount = 0;
            for (int i = 0; i < 20; i++)
            {
                var list = new List<HybridSearchResult>();
                await foreach (var r in _service.SearchAsync("termoEstresseWatcher", ct: cts.Token))
                {
                    list.Add(r);
                }

                if (list.Any(r => r.FilePath == "ancora.md"))
                {
                    foundAncoraCount++;
                }

                await Task.Delay(10, cts.Token);
            }

            return foundAncoraCount;
        }, cts.Token);

        // Assert - nenhuma das tarefas pode travar ou lançar
        await Task.WhenAll(mutationsTask, searchesTask);

        (await searchesTask).ShouldBe(20);
    }

    [Fact]
    public async Task SearchAsync_ReflectsWatcherUpdates()
    {
        // Arrange
        await _service.InitializeAsync(_tempVaultDir);

        // Act - Adiciona nova nota pós-inicialização
        var novaNota = Path.Combine(_tempVaultDir, "nova_pos_init.md");
        await File.WriteAllTextAsync(novaNota, "Conteudo criado pos inicializacao com termoRefletidoWatcher.");

        // Aguarda debounce do watcher (50ms)
        await Task.Delay(250);

        // Assert
        var results = await SearchAsync("termoRefletidoWatcher");
        results.Count.ShouldBe(1);
        results[0].FilePath.ShouldBe("nova_pos_init.md");
        results[0].Source.ShouldBe(SearchMatchSource.IndexOnly);
    }

    [Fact]
    public async Task SearchAsync_WhenWatcherFails_MarksIndexNotReadyAndFallsBackToRipgrep()
    {
        // Arrange
        var note = Path.Combine(_tempVaultDir, "resiliencia.md");
        await File.WriteAllTextAsync(note, "Conteudo exclusivo com palavraResilienciaTotal no disco.");

        var mockWatcher = Substitute.For<IVaultIndexWatcher>();
        mockWatcher.IsRunning.Returns(true);
        mockWatcher.HasFailed.Returns(false);

        var dedicatedDbPath = Path.Combine(Path.GetTempPath(), $"synapse-watcher-fail-{Guid.NewGuid():N}.db");
        var searchIndex = SqliteVaultSearchIndex.ForFile(dedicatedDbPath);
        var rawSearchEngine = new RipgrepSearchEngine();
        var bulkIndexer = new VaultBulkIndexer(10);

        try
        {
            using var service = new HybridSearchService(
                searchIndex,
                rawSearchEngine,
                bulkIndexer,
                mockWatcher,
                ownsDependencies: false);

            await service.InitializeAsync(_tempVaultDir);

            // 1. Com watcher saudável, índice está pronto e a busca comum usa FTS5 sozinho (IndexOnly)
            service.IsIndexReady.ShouldBeTrue();
            var resultsBefore = new List<HybridSearchResult>();
            await foreach (var r in service.SearchAsync("palavraResilienciaTotal"))
            {
                resultsBefore.Add(r);
            }
            resultsBefore.Count.ShouldBe(1);
            resultsBefore[0].Source.ShouldBe(SearchMatchSource.IndexOnly);

            // 2. Act - Simula morte/falha do watcher (ex: estouro de buffer do FileSystemWatcher)
            mockWatcher.HasFailed.Returns(true);
            mockWatcher.ErrorOccurred += Raise.Event<EventHandler<Exception>>(
                mockWatcher,
                new IOException("Estouro de buffer do FileSystemWatcher simulado"));

            // 3. Assert - O índice deixa imediatamente de ser considerado pronto
            service.IsIndexReady.ShouldBeFalse();

            // 4. A busca degrada automaticamente para o Ripgrep sozinho como contingência (RipgrepOnly)
            var resultsAfter = new List<HybridSearchResult>();
            await foreach (var r in service.SearchAsync("palavraResilienciaTotal"))
            {
                resultsAfter.Add(r);
            }
            resultsAfter.Count.ShouldBe(1);
            resultsAfter[0].Source.ShouldBe(SearchMatchSource.RipgrepOnly);
        }
        finally
        {
            searchIndex.Dispose();
            try { if (File.Exists(dedicatedDbPath)) File.Delete(dedicatedDbPath); } catch { }
        }
    }

    [Fact]
    public async Task EditingFileIndexedByBulk_DoesNotLeaveStaleContentSearchable()
    {
        // Arrange - arquivo ja existe ANTES do bulk index, entao entra no indice pela via do
        // VaultBulkIndexer; depois e editado, entrando pela via do watcher. As duas vias
        // precisam usar a mesma chave de file_path, senao a linha antiga sobrevive e o termo
        // apagado continua sendo encontrado.
        var nota = Path.Combine(_tempVaultDir, "editada.md");
        await File.WriteAllTextAsync(nota, "Nota com termoQueSeraApagado no conteudo.");

        await _service.InitializeAsync(_tempVaultDir);

        (await SearchAsync("termoQueSeraApagado")).Count.ShouldBe(1);

        // Act - usuario apaga o termo do arquivo
        await File.WriteAllTextAsync(nota, "Nota reescrita com conteudoTotalmenteNovo.");
        await Task.Delay(400);

        // Assert - o termo apagado nao pode mais ser encontrado
        (await SearchAsync("termoQueSeraApagado")).ShouldBeEmpty();

        // E o conteudo novo tem que ser encontravel
        (await SearchAsync("conteudoTotalmenteNovo")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task SearchAsync_BeforeInitialization_ThrowsInvalidOperationException()
    {
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in _service.SearchAsync("qualquer_termo"))
            {
            }
        });
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
