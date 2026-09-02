using Shouldly;
using Synapse.Search;
using Xunit;

namespace Synapse.Tests.Search;

/// <summary>
/// O uso real e indexacao em background acontecendo enquanto a UI busca. Antes, uma
/// SqliteConnection unica era compartilhada por todas as operacoes, e SqliteConnection
/// nao e thread-safe. Estes testes cobrem esse encontro.
/// </summary>
public sealed class SqliteVaultSearchIndexConcurrencyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"synapse-fts-conc-{Guid.NewGuid():N}");

    // A raiz e criada aqui de proposito: so o teste de diretorio deve depender de uma
    // pasta ausente, e ele usa um subdiretorio ainda mais fundo. Sem isso, os testes de
    // concorrencia falhariam por falta de pasta e nao pelo que pretendem medir.
    public SqliteVaultSearchIndexConcurrencyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void ForFile_CriaODiretorioQuandoEleNaoExiste()
    {
        var dbPath = Path.Combine(_dir, "search", "index.db");
        Directory.Exists(Path.GetDirectoryName(dbPath)!).ShouldBeFalse();

        using var index = SqliteVaultSearchIndex.ForFile(dbPath);

        File.Exists(dbPath).ShouldBeTrue();
    }

    [Fact]
    public async Task BuscaEIndexacaoAoMesmoTempo_NaoQuebram()
    {
        var dbPath = Path.Combine(_dir, "index.db");
        using var index = SqliteVaultSearchIndex.ForFile(dbPath);

        // Uma nota conhecida, para a busca ter sempre o que achar durante a escrita.
        await index.IndexFileAsync("ancora.md", "documento ancora do teste de concorrencia");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var escrita = Task.Run(async () =>
        {
            for (var lote = 0; lote < 12; lote++)
            {
                var itens = Enumerable.Range(0, 40)
                    .Select(i => ($"nota-{lote}-{i}.md", $"conteudo do lote {lote} item {i} com texto suficiente para indexar"))
                    .ToList();

                await index.IndexBatchAsync(itens, cts.Token);
            }
        }, cts.Token);

        var leitura = Task.Run(async () =>
        {
            var achouAncora = 0;
            for (var i = 0; i < 60; i++)
            {
                await foreach (var r in index.SearchAsync("ancora", ct: cts.Token))
                {
                    if (r.FilePath == "ancora.md")
                    {
                        achouAncora++;
                    }
                }
            }

            return achouAncora;
        }, cts.Token);

        // Nenhuma das duas pode lancar: e isso que a conexao compartilhada quebrava.
        await Task.WhenAll(escrita, leitura);

        (await leitura).ShouldBe(60, "toda busca deve enxergar a ancora, mesmo com escrita em curso");
        (await index.GetIndexedFileCountAsync()).ShouldBe(1 + (12 * 40));
    }

    [Fact]
    public async Task DoisLotesAoMesmoTempo_NaoColidem()
    {
        var dbPath = Path.Combine(_dir, "lotes.db");
        using var index = SqliteVaultSearchIndex.ForFile(dbPath);

        // Este e o encontro que quebra de verdade numa conexao compartilhada: a segunda
        // transacao acha a primeira ainda aberta, e o Microsoft.Data.Sqlite recusa
        // transacao aninhada. Com uma conexao por operacao, o proprio SQLite serializa
        // as duas escritas (busy_timeout cobre a espera).
        static List<(string FilePath, string Content)> Lote(string prefixo) =>
            Enumerable.Range(0, 800)
                .Select(i => ($"{prefixo}-{i}.md", $"conteudo {i} do lote {prefixo} com texto suficiente para indexar"))
                .ToList();

        await Task.WhenAll(
            Task.Run(() => index.IndexBatchAsync(Lote("a"))),
            Task.Run(() => index.IndexBatchAsync(Lote("b"))));

        (await index.GetIndexedFileCountAsync()).ShouldBe(1600);
    }

    [Fact]
    public async Task BuscaInterrompidaNoMeio_LiberaOArquivo()
    {
        var dbPath = Path.Combine(_dir, "abandono.db");
        var index = SqliteVaultSearchIndex.ForFile(dbPath);

        await index.IndexBatchAsync(
            Enumerable.Range(0, 20).Select(i => ($"n{i}.md", "termo repetido em todas as notas")).ToList());

        // Para a enumeracao no primeiro resultado: o using dentro do iterador tem que
        // fechar a conexao mesmo assim, senao o arquivo fica preso.
        await foreach (var _ in index.SearchAsync("termo"))
        {
            break;
        }

        index.Dispose();

        Should.NotThrow(() => File.Delete(dbPath));
    }
}
