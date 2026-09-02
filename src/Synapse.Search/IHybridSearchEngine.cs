namespace Synapse.Search;

/// <summary>
/// Contrato para o motor de busca híbrido do Synapse que combina busca em disco via
/// Ripgrep e busca indexada em SQLite FTS5 via Reciprocal Rank Fusion.
/// </summary>
public interface IHybridSearchEngine
{
    /// <summary>
    /// Executa uma busca híbrida sobre o cofre, fundindo resultados do Ripgrep e do índice FTS5.
    /// </summary>
    /// <param name="vaultRootPath">Caminho absoluto da raiz do cofre.</param>
    /// <param name="query">Termo ou padrão de busca.</param>
    /// <param name="isRegex">Indica se o padrão deve ser avaliado como regex no Ripgrep.</param>
    /// <param name="limit">Quantidade máxima de resultados a retornar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Fluxo incremental ordenado por relevância RRF decrescente.</returns>
    IAsyncEnumerable<HybridSearchResult> SearchAsync(
        string vaultRootPath,
        string query,
        bool isRegex = false,
        int limit = 100,
        CancellationToken ct = default);
}
