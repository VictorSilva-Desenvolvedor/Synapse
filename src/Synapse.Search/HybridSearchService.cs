namespace Synapse.Search;

/// <summary>
/// Implementação unificada do serviço de busca híbrida do Synapse,
/// combinando indexação em lote inicial, monitoramento incremental em tempo real (watcher)
/// e busca híbrida assíncrona com ranking RRF.
/// </summary>
public sealed class HybridSearchService : IHybridSearchService
{
    private readonly IVaultSearchIndex _searchIndex;
    private readonly IRawSearchEngine _rawSearchEngine;
    private readonly IVaultBulkIndexer _bulkIndexer;
    private readonly IVaultIndexWatcher _indexWatcher;
    private readonly IHybridSearchEngine _hybridEngine;
    private readonly bool _ownsDependencies;

    private string? _vaultRootPath;
    private bool _isDisposed;

    public bool IsInitialized => _vaultRootPath != null;
    public bool IsBulkIndexing { get; private set; }

    public HybridSearchService(
        IVaultSearchIndex searchIndex,
        IRawSearchEngine rawSearchEngine,
        IVaultBulkIndexer bulkIndexer,
        IVaultIndexWatcher indexWatcher,
        IHybridSearchEngine? hybridEngine = null,
        bool ownsDependencies = false)
    {
        _searchIndex = searchIndex ?? throw new ArgumentNullException(nameof(searchIndex));
        _rawSearchEngine = rawSearchEngine ?? throw new ArgumentNullException(nameof(rawSearchEngine));
        _bulkIndexer = bulkIndexer ?? throw new ArgumentNullException(nameof(bulkIndexer));
        _indexWatcher = indexWatcher ?? throw new ArgumentNullException(nameof(indexWatcher));
        _hybridEngine = hybridEngine ?? new HybridSearchEngine(_searchIndex, _rawSearchEngine);
        _ownsDependencies = ownsDependencies;
    }

    /// <summary>
    /// Fábrica conveniente para criar um HybridSearchService completo com todas as dependências
    /// apontadas para um arquivo de banco SQLite local.
    /// </summary>
    public static HybridSearchService ForVault(
        string databaseFilePath,
        string? customRgPath = null,
        TimeSpan? watcherDebounce = null,
        int bulkBatchSize = 500)
    {
        var searchIndex = SqliteVaultSearchIndex.ForFile(databaseFilePath);
        var rawSearchEngine = new RipgrepSearchEngine(customRgPath);
        var bulkIndexer = new VaultBulkIndexer(bulkBatchSize);
        var indexWatcher = new VaultIndexWatcher(searchIndex, watcherDebounce);
        var hybridEngine = new HybridSearchEngine(searchIndex, rawSearchEngine);

        return new HybridSearchService(
            searchIndex,
            rawSearchEngine,
            bulkIndexer,
            indexWatcher,
            hybridEngine,
            ownsDependencies: true);
    }

    public async Task InitializeAsync(
        string vaultRootPath,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath))
        {
            throw new ArgumentException("Vault root path cannot be null or whitespace.", nameof(vaultRootPath));
        }

        if (!Directory.Exists(vaultRootPath))
        {
            throw new DirectoryNotFoundException($"Vault root directory does not exist: '{vaultRootPath}'");
        }

        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(HybridSearchService));
        }

        _vaultRootPath = Path.GetFullPath(vaultRootPath);

        // 1. Inicia o watcher imediatamente para que mutações no cofre sejam capturadas
        _indexWatcher.Start(_vaultRootPath);

        // 2. Executa a indexação em lote (bulk index)
        IsBulkIndexing = true;
        try
        {
            await _bulkIndexer.IndexVaultAsync(_vaultRootPath, _searchIndex, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            IsBulkIndexing = false;
        }
    }

    public IAsyncEnumerable<HybridSearchResult> SearchAsync(
        string query,
        bool isRegex = false,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(HybridSearchService));
        }

        if (_vaultRootPath == null)
        {
            throw new InvalidOperationException("HybridSearchService is not initialized. Call InitializeAsync first.");
        }

        return _hybridEngine.SearchAsync(_vaultRootPath, query, isRegex, limit, ct);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _indexWatcher.Stop();

        if (_ownsDependencies)
        {
            _indexWatcher.Dispose();
            _searchIndex.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
