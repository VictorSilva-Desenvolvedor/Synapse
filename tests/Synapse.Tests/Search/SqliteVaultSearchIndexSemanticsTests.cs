using Shouldly;
using Synapse.Search;
using Xunit;

namespace Synapse.Tests.Search;

/// <summary>
/// Fixa a semantica de uma busca com mais de uma palavra. Nenhum outro teste usa mais
/// de um termo, e a primeira versao envolvia a query inteira em aspas — o que a tornava
/// uma FRASE e fazia "sync conflito" nao achar uma nota que continha as duas palavras
/// separadas. Estes testes existem para que essa regressao nao volte silenciosa.
/// </summary>
public sealed class SqliteVaultSearchIndexSemanticsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"synapse-fts-sem-{Guid.NewGuid():N}.db");
    private readonly SqliteVaultSearchIndex _index;

    public SqliteVaultSearchIndexSemanticsTests() => _index = SqliteVaultSearchIndex.ForFile(_dbPath);

    public void Dispose()
    {
        _index.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task<List<VaultSearchResult>> SearchAsync(string query)
    {
        var results = new List<VaultSearchResult>();
        await foreach (var r in _index.SearchAsync(query))
        {
            results.Add(r);
        }

        return results;
    }

    [Fact]
    public async Task DuasPalavras_JuntasNoTexto_Encontram()
    {
        await _index.IndexFileAsync("a.md", "O motor de busca hibrido do Synapse.");

        (await SearchAsync("busca hibrido")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task DuasPalavras_SeparadasNoTexto_TambemEncontram()
    {
        await _index.IndexFileAsync("a.md", "A busca varre o cofre inteiro. Mais adiante, o indice hibrido junta os dois motores.");

        (await SearchAsync("busca hibrido")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task UmDosTermosAusente_NaoEncontra()
    {
        // AND, nao OR: faltando um termo, o documento fica de fora.
        await _index.IndexFileAsync("a.md", "A busca varre o cofre inteiro.");

        (await SearchAsync("busca inexistente")).ShouldBeEmpty();
    }

    [Fact]
    public async Task OrdemDosTermosNaoImporta()
    {
        await _index.IndexFileAsync("a.md", "primeiro alfa, depois bravo.");

        (await SearchAsync("bravo alfa")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task OperadorDeTermoContinuaLiteral()
    {
        // Cada termo continua entre aspas, entao AND/OR/NOT nunca viram operador.
        await _index.IndexFileAsync("a.md", "clausula AND usada como palavra comum.");
        await _index.IndexFileAsync("b.md", "texto sem a palavra reservada.");

        var results = await SearchAsync("clausula AND");

        results.Count.ShouldBe(1);
        results[0].FilePath.ShouldBe("a.md");
    }

    [Fact]
    public async Task PrefixoNaoFunciona_PorqueAsteriscoContinuaLiteral()
    {
        await _index.IndexFileAsync("a.md", "sincronizacao incremental");

        (await SearchAsync("sincroniz*")).ShouldBeEmpty();
    }
}
