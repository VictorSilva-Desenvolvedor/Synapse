using System.Threading.Channels;
using Synapse.Core.Ports;
using Synapse.Host.Ipc;
using Synapse.Rules;
using Synapse.Sync;
using Synapse.Sync.Auth;
using Synapse.Sync.GitHub;
using Synapse.Sync.Ignore;
using Synapse.Sync.Reconciliation;

namespace Synapse.Host;

/// <summary>
/// Worker de orquestração do serviço Synapse em segundo plano (ADR-006, ADR-010).
/// Orquestra a execução contínua de:
/// - FileWatcherService + Debouncer (alterações locais do cofre)
/// - RemoteChangesPoller (mudanças remotas do GitHub)
/// - ReconciliationJob (reconciliação de integridade periódica)
/// - SyncQueueProcessor (consumidor serializado single-writer)
/// - IpcServer (comunicação com a bandeja via Named Pipe)
/// - IRuleEngine + RuleExecutor (automação e hot-reload de .synapse/regras.yaml)
/// - SynapseIgnoreMatcher (lista de exclusão configurável e hot-reload de .synapseignore - US-SYNC.7)
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ISyncIndexStore _indexStore;
    private readonly ICloudProvider _cloudProvider;
    private readonly IConflictResolver _conflictResolver;
    private readonly IFileSystem _fileSystem;
    private readonly IRuleEngine _ruleEngine;
    private readonly GitHubAuthManager _authManager;
    private readonly GitHubClientConfig _gitHubConfig;
    private readonly IVaultWatcher _vaultWatcher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<Worker> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private readonly Channel<VaultChangeEvent> _eventChannel = Channel.CreateUnbounded<VaultChangeEvent>();
    private readonly SynapseIgnoreMatcher _ignoreMatcher = new();
    private Debouncer? _debouncer;
    private SyncQueueProcessor? _processor;
    private RemoteChangesPoller? _poller;
    private ReconciliationJob? _reconciliationJob;
    private IpcServer? _ipcServer;
    private RuleExecutor? _ruleExecutor;
    private string _vaultPath = string.Empty;
    private string _rulesFilePath = string.Empty;
    private string _ignoreFilePath = string.Empty;

    private string _status = "Sincronizado";
    private bool _isPaused;
    private DateTimeOffset? _lastSyncedAt;
    private int _pendingItems;

    public Worker(
        ISyncIndexStore indexStore,
        ICloudProvider cloudProvider,
        IConflictResolver conflictResolver,
        IFileSystem fileSystem,
        IRuleEngine ruleEngine,
        GitHubAuthManager authManager,
        GitHubClientConfig gitHubConfig,
        IVaultWatcher vaultWatcher,
        IConfiguration configuration,
        ILogger<Worker> logger,
        ILoggerFactory loggerFactory)
    {
        _indexStore = indexStore ?? throw new ArgumentNullException(nameof(indexStore));
        _cloudProvider = cloudProvider ?? throw new ArgumentNullException(nameof(cloudProvider));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
        _gitHubConfig = gitHubConfig ?? throw new ArgumentNullException(nameof(gitHubConfig));
        _vaultWatcher = vaultWatcher ?? throw new ArgumentNullException(nameof(vaultWatcher));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Iniciando serviço Synapse...");

        var synapseConfig = _configuration.GetSection("Synapse");
        _vaultPath = synapseConfig["VaultPath"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SynapseVault");
        var baseCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Synapse", "base_cache");
        _rulesFilePath = Path.Combine(_vaultPath, ".synapse", "regras.yaml");
        _ignoreFilePath = Path.Combine(_vaultPath, ".synapseignore");

        Directory.CreateDirectory(_vaultPath);
        Directory.CreateDirectory(baseCachePath);

        // 1. Carrega lista de exclusão (.synapseignore - US-SYNC.7)
        _ignoreMatcher.LoadFromFile(_ignoreFilePath);
        _logger.LogInformation("Lista de exclusão (.synapseignore) carregada com sucesso.");

        _ruleExecutor = new RuleExecutor(_fileSystem, _vaultPath);

        // 2. Carrega regras iniciais se existirem (RF-RULES.1)
        if (File.Exists(_rulesFilePath))
        {
            try
            {
                await _ruleEngine.LoadRulesAsync(_rulesFilePath, stoppingToken);
                _logger.LogInformation("Regras de automação carregadas com sucesso de {RulesPath}", _rulesFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao carregar regras iniciais de {RulesPath}", _rulesFilePath);
            }
        }

        var processorOptions = new SyncQueueProcessorOptions(_vaultPath, string.Empty, baseCachePath);
        _processor = new SyncQueueProcessor(
            _cloudProvider,
            _indexStore,
            _conflictResolver,
            _fileSystem,
            processorOptions);

        _debouncer = new Debouncer(evt =>
        {
            _ = _eventChannel.Writer.WriteAsync(evt);
        }, TimeSpan.FromMilliseconds(2000));

        _vaultWatcher.Changed += (_, evt) =>
        {
            _debouncer.OnRawEvent(evt);
        };

        _vaultWatcher.Start(_vaultPath);
        _logger.LogInformation("Monitoramento local ativo no cofre: {VaultPath}", _vaultPath);

        _poller = new RemoteChangesPoller(
            _cloudProvider,
            _indexStore,
            async (evt, ct) =>
            {
                if (!_ignoreMatcher.ShouldIgnore(evt.RelativePath))
                {
                    await _eventChannel.Writer.WriteAsync(evt, ct);
                }
            },
            TimeSpan.FromSeconds(60));

        _reconciliationJob = new ReconciliationJob(
            _vaultPath,
            _indexStore,
            _fileSystem,
            _eventChannel.Writer,
            TimeSpan.FromMinutes(15),
            ignoreMatcher: _ignoreMatcher,
            logger: _loggerFactory.CreateLogger<ReconciliationJob>());

        _ipcServer = new IpcServer(
            getStatusHandler: () => new IpcStatusPayload
            {
                Estado = _status,
                Pausado = _isPaused,
                UltimaSincronizacaoEm = _lastSyncedAt,
                ItensPendentes = _pendingItems
            },
            pauseHandler: () =>
            {
                _isPaused = true;
                _logger.LogInformation("Sincronização pausada pelo usuário via IPC.");
                return Task.FromResult(new IpcStatusPayload
                {
                    Estado = _status,
                    Pausado = _isPaused,
                    UltimaSincronizacaoEm = _lastSyncedAt,
                    ItensPendentes = _pendingItems
                });
            },
            resumeHandler: () =>
            {
                _isPaused = false;
                _logger.LogInformation("Sincronização retomada pelo usuário via IPC.");
                return Task.FromResult(new IpcStatusPayload
                {
                    Estado = _status,
                    Pausado = _isPaused,
                    UltimaSincronizacaoEm = _lastSyncedAt,
                    ItensPendentes = _pendingItems
                });
            },
            reconnectHandler: async () =>
            {
                try
                {
                    var token = await _authManager.GetValidTokenAsync(stoppingToken);
                    var valid = await _authManager.ValidateTokenAsync(token, stoppingToken);
                    _status = valid ? "Sincronizado" : "AuthRequired";
                }
                catch
                {
                    _status = "AuthRequired";
                }

                return new IpcStatusPayload
                {
                    Estado = _status,
                    Pausado = _isPaused,
                    UltimaSincronizacaoEm = _lastSyncedAt,
                    ItensPendentes = _pendingItems
                };
            },
            getLogPathHandler: () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Synapse", "logs"),
            logger: _loggerFactory.CreateLogger<IpcServer>());

        // Inicia componentes concorrentes
        var tasks = new List<Task>
        {
            Task.Run(() => _ipcServer.StartAsync(stoppingToken), stoppingToken),
            Task.Run(() => ProcessEventQueueLoopAsync(stoppingToken), stoppingToken),
            Task.Run(() => RunRemotePollingLoopAsync(stoppingToken), stoppingToken),
            Task.Run(() => _reconciliationJob.RunAsync(stoppingToken), stoppingToken)
        };

        _logger.LogInformation("Synapse Worker executando com sucesso.");

        await Task.WhenAll(tasks);
    }

    private async Task ProcessEventQueueLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Enfileira todos os eventos recebidos no canal para a tabela SQLite
                while (_eventChannel.Reader.TryRead(out var evt))
                {
                    // 1. Hot-reload de .synapseignore (US-SYNC.7)
                    if (evt.RelativePath.Equals(".synapseignore", StringComparison.OrdinalIgnoreCase))
                    {
                        _ignoreMatcher.LoadFromFile(_ignoreFilePath);
                        _logger.LogInformation("Lista de exclusão (.synapseignore) recarregada dinamicamente.");
                        continue;
                    }

                    // 2. Hot-reload do arquivo de regras (RF-RULES.1)
                    if (evt.RelativePath.Equals(".synapse/regras.yaml", StringComparison.OrdinalIgnoreCase) ||
                        evt.RelativePath.Equals(".synapse/regras.yml", StringComparison.OrdinalIgnoreCase) ||
                        evt.RelativePath.Equals(".synapse\\regras.yaml", StringComparison.OrdinalIgnoreCase) ||
                        evt.RelativePath.Equals(".synapse\\regras.yml", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            await Task.Delay(200, ct); // Pequeno debounce de I/O
                            await _ruleEngine.LoadRulesAsync(_rulesFilePath, ct);
                            _logger.LogInformation("Regras de automação recarregadas dinamicamente de {RulesPath}", _rulesFilePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Falha ao recarregar regras dinâmicas de {RulesPath}", _rulesFilePath);
                        }
                        continue;
                    }

                    // 3. Checagem de exclusão configurável e limite de tamanho (US-SYNC.7)
                    var noteFullPath = Path.Combine(_vaultPath, evt.RelativePath);
                    long? fileSizeBytes = null;
                    if (File.Exists(noteFullPath))
                    {
                        fileSizeBytes = new FileInfo(noteFullPath).Length;
                    }

                    if (_ignoreMatcher.ShouldIgnore(evt.RelativePath, fileSizeBytes))
                    {
                        _logger.LogDebug("Arquivo ignorado pela lista de exclusão: {Path}", evt.RelativePath);
                        continue;
                    }

                    // 4. Avaliação de regras de notas locais (RF-RULES.2-5)
                    if ((evt.EventType is SyncEventType.Created or SyncEventType.Modified) &&
                        evt.RelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (await _fileSystem.ExistsAsync(noteFullPath, ct))
                            {
                                var content = await _fileSystem.ReadAllTextAsync(noteFullPath, ct);
                                var (frontmatter, body) = NoteContentSplitter.Split(content);
                                var noteCtx = new NoteContext(evt.RelativePath, frontmatter, DateTimeOffset.UtcNow);
                                var actions = await _ruleEngine.EvaluateAsync(noteCtx, ct);

                                foreach (var action in actions)
                                {
                                    _logger.LogInformation("Aplicando regra de automação: {Action}", action);
                                    await _ruleExecutor!.ExecuteActionAsync(action, ct);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Falha ao avaliar regras para nota {Path}", evt.RelativePath);
                        }
                    }

                    await _processor!.EnqueueAsync(evt, ct);
                    _pendingItems++;
                }

                if (!_isPaused)
                {
                    var item = await _indexStore.PeekNextAsync(ct);
                    if (item != null)
                    {
                        _status = "Sincronizando";
                        await _processor!.DrainAsync(ct);
                        _lastSyncedAt = DateTimeOffset.UtcNow;
                        _status = "Sincronizado";
                        _pendingItems = 0;
                    }
                }

                // Aguarda novo evento ou timer
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (CloudAuthExpiredException)
            {
                _status = "AuthRequired";
                _logger.LogWarning("Autenticação necessária com o GitHub.");
                await Task.Delay(5000, ct);
            }
            catch (CloudQuotaExceededException)
            {
                _status = "Offline";
                _logger.LogWarning("Limite de taxa da GitHub API atingido. Aguardando...");
                await Task.Delay(10000, ct);
            }
            catch (Exception ex)
            {
                _status = "Erro";
                _logger.LogError(ex, "Erro no processamento da fila de sincronização.");
                await Task.Delay(3000, ct);
            }
        }
    }

    private async Task RunRemotePollingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_isPaused)
                {
                    await _poller!.RunOnceAsync(ct);
                }

                await Task.Delay(TimeSpan.FromSeconds(60), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Falha durante o polling de mudanças remotas do GitHub.");
                await Task.Delay(10000, ct);
            }
        }
    }

    public override void Dispose()
    {
        _vaultWatcher.Stop();
        _debouncer?.Dispose();
        base.Dispose();
    }
}
