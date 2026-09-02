using NSubstitute;
using Shouldly;
using Synapse.Search;

namespace Synapse.Tests.Search;

public sealed class HybridSearchEngineTests : IDisposable
{
    private readonly string _tempVaultDir;
    private readonly string _tempDbPath;
    private readonly SqliteVaultSearchIndex _searchIndex;
    private readonly RipgrepSearchEngine _rawSearchEngine;
    private readonly HybridSearchEngine _hybridEngine;

    public HybridSearchEngineTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-hybrid-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);

        _tempDbPath = Path.Combine(Path.GetTempPath(), $"synapse-hybrid-db-{Guid.NewGuid():N}.db");
        _searchIndex = SqliteVaultSearchIndex.ForFile(_tempDbPath);
        _rawSearchEngine = new RipgrepSearchEngine();
        _hybridEngine = new HybridSearchEngine(_searchIndex, _rawSearchEngine);
    }

    public void Dispose()
    {
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

    private async Task<List<HybridSearchResult>> SearchAsync(string query, bool isRegex = false, int limit = 100, CancellationToken ct = default)
    {
        var list = new List<HybridSearchResult>();
        await foreach (var item in _hybridEngine.SearchAsync(_tempVaultDir, query, isRegex, limit, ct))
        {
            list.Add(item);
        }
        return list;
    }

    [Fact]
    public async Task SearchAsync_WhenIndexReady_ReturnsResultsFromIndexOnlyInMilliseconds()
    {
        // Arrange
        // Doc 1: presente em ambos (disco + índice FTS5)
        var file1 = Path.Combine(_tempVaultDir, "doc1.md");
        await File.WriteAllTextAsync(file1, "Primeiro documento com palavraChaveUnica em destaque.");
        await _searchIndex.IndexFileAsync("doc1.md", "Primeiro documento com palavraChaveUnica em destaque.");

        // Doc 2: presente apenas no índice (simulando alteração no disco que removeu a palavra)
        var file2 = Path.Combine(_tempVaultDir, "doc2.md");
        await File.WriteAllTextAsync(file2, "Segundo documento modificado sem a palavra.");
        await _searchIndex.IndexFileAsync("doc2.md", "Segundo documento antigo com palavraChaveUnica.");

        // Act
        var results = await SearchAsync("palavraChaveUnica");

        // Assert: com índice pronto, o FTS5 responde ambos como IndexOnly
        results.Count.ShouldBe(2);
        results.All(r => r.Source == SearchMatchSource.IndexOnly).ShouldBeTrue();
        results.Select(r => r.FilePath).ShouldContain("doc1.md");
        results.Select(r => r.FilePath).ShouldContain("doc2.md");
    }

    [Fact]
    public async Task SearchAsync_WhenFileCreatedAfterIndexing_ReturnsRipgrepOnlyWhenIndexNotReady()
    {
        // Arrange
        // Popula o índice FTS5 com um arquivo qualquer
        await _searchIndex.IndexFileAsync("antigo.md", "Documento antigo sem relacao.");

        // Cria um arquivo novo no disco que ainda não foi indexado
        var novoFile = Path.Combine(_tempVaultDir, "novo.md");
        await File.WriteAllTextAsync(novoFile, "Nota recem-criada com termoNovoAoVivo no disco.");

        // Simula estado em que o índice não está pronto (contingência Ripgrep)
        _hybridEngine.IsIndexReady = () => false;

        // Act
        var results = await SearchAsync("termoNovoAoVivo");

        // Assert
        results.Count.ShouldBe(1);
        results[0].FilePath.ShouldBe("novo.md");
        results[0].Source.ShouldBe(SearchMatchSource.RipgrepOnly);
        results[0].RipgrepMatches.Count.ShouldBe(1);
        results[0].Score.ShouldBeGreaterThan(0.0);
    }

    [Fact]
    public async Task SearchAsync_WhenFileDeletedAfterIndexing_FiltersOutDeletedFile()
    {
        // Arrange
        // Indexa um arquivo no SQLite FTS5 que NÃO existe fisicamente no disco
        await _searchIndex.IndexFileAsync("fantasma.md", "Documento fantasma com palavraChaveFantasma indexada.");

        // Garante que o arquivo de fato não existe no disco
        File.Exists(Path.Combine(_tempVaultDir, "fantasma.md")).ShouldBeFalse();

        // Act
        var results = await SearchAsync("palavraChaveFantasma");

        // Assert: o motor deve descartar o arquivo fantasma mesmo no despacho de FTS5
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithDiverseSemantics_DispatchesToIndexOnly()
    {
        // Arrange
        var file1 = Path.Combine(_tempVaultDir, "exato.md");
        await File.WriteAllTextAsync(file1, "Nota contendo sync conflito na mesma linha.");
        await _searchIndex.IndexFileAsync("exato.md", "Nota contendo sync conflito na mesma linha.");

        var file2 = Path.Combine(_tempVaultDir, "separado.md");
        await File.WriteAllTextAsync(file2, "O conflito de notas foi gerado durante o processo de sync.");
        await _searchIndex.IndexFileAsync("separado.md", "O conflito de notas foi gerado durante o processo de sync.");

        // Act
        var results = await SearchAsync("sync conflito");

        // Assert: com índice pronto, FTS5 responde ambos como IndexOnly
        results.Count.ShouldBe(2);

        var exato = results.First(r => r.FilePath == "exato.md");
        exato.Source.ShouldBe(SearchMatchSource.IndexOnly);

        var separado = results.First(r => r.FilePath == "separado.md");
        separado.Source.ShouldBe(SearchMatchSource.IndexOnly);
    }

    [Fact]
    public async Task SearchAsync_WhenPathsHaveDifferentFormats_NormalizesToCanonicalPath()
    {
        // Arrange
        var subDir = Path.Combine(_tempVaultDir, "sub", "pasta");
        Directory.CreateDirectory(subDir);
        var note = Path.Combine(subDir, "documento.md");
        await File.WriteAllTextAsync(note, "Texto com termoCasamentoDeCaminho.");

        // Indexa no FTS5 usando caminho absoluto do Windows com barras invertidas
        await _searchIndex.IndexFileAsync(note, "Texto com termoCasamentoDeCaminho.");

        // Act
        var results = await SearchAsync("termoCasamentoDeCaminho");

        // Assert
        results.Count.ShouldBe(1);
        results[0].FilePath.ShouldBe("sub/pasta/documento.md");
        results[0].Source.ShouldBe(SearchMatchSource.IndexOnly);
    }

    [Fact]
    public async Task SearchAsync_WhenNoResultsInEitherEngine_ReturnsEmptyWithoutThrowing()
    {
        // Arrange
        var note = Path.Combine(_tempVaultDir, "note.md");
        await File.WriteAllTextAsync(note, "Texto qualquer");
        await _searchIndex.IndexFileAsync("note.md", "Texto qualquer");

        // Act
        var results = await SearchAsync("termoInexistenteEmAmbos_9999");

        // Assert
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithIndexReady_DoesNotInvokeRawSearchEngineAtAll()
    {
        // Arrange
        var notePath = Path.Combine(_tempVaultDir, "rapido.md");
        await File.WriteAllTextAsync(notePath, "Conteúdo da nota rápida com termoEspiao.");

        var mockIndex = Substitute.For<IVaultSearchIndex>();
        mockIndex.SearchAsync("termoEspiao", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable([
                new VaultSearchResult("rapido.md", "snippet", -1.0)
            ]));

        var spyRawEngine = Substitute.For<IRawSearchEngine>();
        var engine = new HybridSearchEngine(mockIndex, spyRawEngine, () => true);

        // Act
        var results = new List<HybridSearchResult>();
        await foreach (var r in engine.SearchAsync(_tempVaultDir, "termoEspiao"))
        {
            results.Add(r);
        }

        // Assert: Prova mandatória - com índice pronto, o Ripgrep NÃO é invocado nenhuma vez
        spyRawEngine.DidNotReceiveWithAnyArgs().SearchAsync(default!, default!, default!, default!);
        results.Count.ShouldBe(1);
        results[0].Source.ShouldBe(SearchMatchSource.IndexOnly);
        results[0].FilePath.ShouldBe("rapido.md");
    }

    [Fact]
    public async Task SearchAsync_WithRegex_InvokesRipgrepAndDoesNotInvokeSearchIndex()
    {
        // Arrange
        var mockIndex = Substitute.For<IVaultSearchIndex>();
        var mockRawEngine = Substitute.For<IRawSearchEngine>();
        mockRawEngine.SearchAsync(_tempVaultDir, "patt.*rn", true, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable([
                new RipgrepMatch("nota.md", 1, "pattern line", 0, 7)
            ]));

        var engine = new HybridSearchEngine(mockIndex, mockRawEngine, () => true);

        // Act
        var results = new List<HybridSearchResult>();
        await foreach (var r in engine.SearchAsync(_tempVaultDir, "patt.*rn", isRegex: true))
        {
            results.Add(r);
        }

        // Assert: Prova mandatória - regex invoca Ripgrep e NÃO toca no índice
        mockIndex.DidNotReceiveWithAnyArgs().SearchAsync(default!, default!, default!);
        mockRawEngine.Received(1).SearchAsync(_tempVaultDir, "patt.*rn", true, Arg.Any<CancellationToken>());
        results.Count.ShouldBe(1);
        results[0].Source.ShouldBe(SearchMatchSource.RipgrepOnly);
    }

    [Fact]
    public async Task SearchAsync_WhenIndexNotReady_RespondsViaRipgrepContingency()
    {
        // Arrange
        var mockIndex = Substitute.For<IVaultSearchIndex>();
        var mockRawEngine = Substitute.For<IRawSearchEngine>();
        mockRawEngine.SearchAsync(_tempVaultDir, "termoContingencia", false, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable([
                new RipgrepMatch("nota.md", 1, "linha com termoContingencia", 0, 10)
            ]));

        var engine = new HybridSearchEngine(mockIndex, mockRawEngine, () => false); // Índice NÃO pronto

        // Act
        var results = new List<HybridSearchResult>();
        await foreach (var r in engine.SearchAsync(_tempVaultDir, "termoContingencia", isRegex: false))
        {
            results.Add(r);
        }

        // Assert: Prova mandatória - índice não pronto responde via Ripgrep sem tocar no índice
        mockIndex.DidNotReceiveWithAnyArgs().SearchAsync(default!, default!, default!);
        mockRawEngine.Received(1).SearchAsync(_tempVaultDir, "termoContingencia", false, Arg.Any<CancellationToken>());
        results.Count.ShouldBe(1);
        results[0].Source.ShouldBe(SearchMatchSource.RipgrepOnly);
    }

    [Fact]
    public async Task SearchAsync_Bm25Ordering_RespectsLimit()
    {
        // Arrange - 5 documentos com repetições crescentes do termo para afetar o rank BM25
        for (int i = 1; i <= 5; i++)
        {
            var p = Path.Combine(_tempVaultDir, $"doc_{i}.md");
            var content = string.Join(" ", Enumerable.Repeat("termoBm25Limit", i));
            await File.WriteAllTextAsync(p, content);
            await _searchIndex.IndexFileAsync($"doc_{i}.md", content);
        }

        // Act - busca com limit = 3
        var results = await SearchAsync("termoBm25Limit", limit: 3);

        // Assert: Prova mandatória - respeita o limit estrito de 3 e preserva a ordenação BM25
        results.Count.ShouldBe(3);
        results.All(r => r.Source == SearchMatchSource.IndexOnly).ShouldBeTrue();

        // O documento 5 tem a maior densidade do termo e deve estar no topo
        results[0].FilePath.ShouldBe("doc_5.md");
        results[0].Score.ShouldBeGreaterThan(results[1].Score);
        results[1].Score.ShouldBeGreaterThan(results[2].Score);
    }

    [Fact]
    public async Task SearchAsync_WhenCancelled_PropagatesCancellationQuickly()
    {
        // Arrange
        for (int i = 0; i < 30; i++)
        {
            var p = Path.Combine(_tempVaultDir, $"doc_{i}.md");
            await File.WriteAllTextAsync(p, $"Conteudo com termoCancelamento repetido varias vezes {i}");
            await _searchIndex.IndexFileAsync($"doc_{i}.md", $"Conteudo com termoCancelamento repetido varias vezes {i}");
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pré-cancelado

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _hybridEngine.SearchAsync(_tempVaultDir, "termoCancelamento", ct: cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task SearchAsync_UnderConcurrentSearchesAndIndexing_RunsWithoutBlocking()
    {
        // Arrange
        var baseFile = Path.Combine(_tempVaultDir, "base.md");
        await File.WriteAllTextAsync(baseFile, "Nota base com palavraConcorrencia garantida.");
        await _searchIndex.IndexFileAsync("base.md", "Nota base com palavraConcorrencia garantida.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Background: indexação contínua de lotes no SQLite
        var indexTask = Task.Run(async () =>
        {
            for (int lote = 0; lote < 10; lote++)
            {
                var batch = Enumerable.Range(0, 30)
                    .Select(i => ($"nota_{lote}_{i}.md", $"Texto do lote {lote} item {i}"))
                    .ToList();

                await _searchIndex.IndexBatchAsync(batch, cts.Token);
            }
        }, cts.Token);

        // Foreground: múltiplas buscas híbridas concorrentes
        var searchTask = Task.Run(async () =>
        {
            int totalFound = 0;
            for (int i = 0; i < 20; i++)
            {
                await foreach (var r in _hybridEngine.SearchAsync(_tempVaultDir, "palavraConcorrencia", ct: cts.Token))
                {
                    if (r.FilePath == "base.md")
                    {
                        totalFound++;
                    }
                }
            }
            return totalFound;
        }, cts.Token);

        await Task.WhenAll(indexTask, searchTask);

        (await searchTask).ShouldBe(20, "todas as 20 buscas híbridas devem encontrar a nota base mesmo sob indexação intensa");
    }

    private static async IAsyncEnumerable<T> AsAsyncEnumerable<T>(IEnumerable<T> items)
    {
        await Task.Yield();
        foreach (var item in items)
        {
            yield return item;
        }
    }
}
