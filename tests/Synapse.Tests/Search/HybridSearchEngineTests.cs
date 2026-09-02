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
    public async Task SearchAsync_WhenTermPresentInBoth_ReturnsBothWithHigherScore()
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

        // Assert
        results.Count.ShouldBe(2);

        // Doc 1 veio de ambos os motores e deve estar no topo com score RRF combinado
        var doc1 = results.First(r => r.FilePath == "doc1.md");
        doc1.Source.ShouldBe(SearchMatchSource.Both);
        doc1.RipgrepMatches.Count.ShouldBe(1);
        doc1.Snippet.ShouldNotBeNullOrEmpty();

        // Doc 2 veio apenas do índice FTS5
        var doc2 = results.First(r => r.FilePath == "doc2.md");
        doc2.Source.ShouldBe(SearchMatchSource.IndexOnly);
        doc2.RipgrepMatches.ShouldBeEmpty();

        // Score do doc presente em ambos deve ser estritamente maior que o do índice isolado
        doc1.Score.ShouldBeGreaterThan(doc2.Score);
        results[0].FilePath.ShouldBe("doc1.md");
    }

    [Fact]
    public async Task SearchAsync_WhenFileCreatedAfterIndexing_ReturnsRipgrepOnly()
    {
        // Arrange
        // Popula o índice FTS5 com um arquivo qualquer
        await _searchIndex.IndexFileAsync("antigo.md", "Documento antigo sem relacao.");

        // Cria um arquivo novo no disco que ainda não foi indexado
        var novoFile = Path.Combine(_tempVaultDir, "novo.md");
        await File.WriteAllTextAsync(novoFile, "Nota recem-criada com termoNovoAoVivo no disco.");

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

        // Assert: o motor híbrido deve descartar o arquivo fantasma
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithDiverseSemantics_IdentifiesIndexOnlyVsBothCorrectly()
    {
        // Arrange
        // Nota 1: contém a frase contínua "sync conflito" (casa no Ripgrep e no FTS5)
        var file1 = Path.Combine(_tempVaultDir, "exato.md");
        await File.WriteAllTextAsync(file1, "Nota contendo sync conflito na mesma linha.");
        await _searchIndex.IndexFileAsync("exato.md", "Nota contendo sync conflito na mesma linha.");

        // Nota 2: contém as duas palavras separadas em posições diferentes (casa no FTS5 AND, mas não como substring no Ripgrep)
        var file2 = Path.Combine(_tempVaultDir, "separado.md");
        await File.WriteAllTextAsync(file2, "O conflito de notas foi gerado durante o processo de sync.");
        await _searchIndex.IndexFileAsync("separado.md", "O conflito de notas foi gerado durante o processo de sync.");

        // Act
        var results = await SearchAsync("sync conflito");

        // Assert
        results.Count.ShouldBe(2);

        var exato = results.First(r => r.FilePath == "exato.md");
        exato.Source.ShouldBe(SearchMatchSource.Both);

        var separado = results.First(r => r.FilePath == "separado.md");
        separado.Source.ShouldBe(SearchMatchSource.IndexOnly);

        // O resultado exato (Both) pontua acima do resultado separado (IndexOnly)
        exato.Score.ShouldBeGreaterThan(separado.Score);
        results[0].FilePath.ShouldBe("exato.md");
    }

    [Fact]
    public async Task SearchAsync_WhenPathsHaveDifferentFormats_MergesIntoBothWithCanonicalPath()
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
        // Deve fundir os dois motores no mesmo caminho canônico normalizado
        results.Count.ShouldBe(1);
        results[0].FilePath.ShouldBe("sub/pasta/documento.md");
        results[0].Source.ShouldBe(SearchMatchSource.Both);
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
}
