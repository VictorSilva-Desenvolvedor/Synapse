namespace Synapse.Search;

/// <summary>
/// Serviço unificado de busca híbrida do Synapse que integra bulk index inicial,
/// monitoramento em tempo real via watcher e buscas híbridas com ranking RRF.
/// </summary>
public interface IHybridSearchService : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Inicializa o serviço no cofre indicado: inicia o watcher em tempo real
    /// e executa a indexação em lote (bulk indexing) em background.
    /// </summary>
    /// <param name="vaultRootPath">Caminho absoluto da raiz do cofre.</param>
    /// <param name="progress">Notificador opcional de progresso da indexação inicial.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task InitializeAsync(string vaultRootPath, IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Executa uma busca híbrida no cofre. Funciona concorrentemente mesmo
    /// durante a indexação em lote ou gravações do watcher.
    /// </summary>
    /// <param name="query">Termo ou padrão de busca.</param>
    /// <param name="isRegex">Indica se a busca no Ripgrep deve usar regex.</param>
    /// <param name="limit">Limite de resultados.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Fluxo assíncrono ordenado por relevância RRF.</returns>
    IAsyncEnumerable<HybridSearchResult> SearchAsync(
        string query,
        bool isRegex = false,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Indica se o serviço já foi inicializado com um cofre.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Indica se a indexação em lote (bulk index) inicial ainda está em andamento.
    /// </summary>
    bool IsBulkIndexing { get; }
}
