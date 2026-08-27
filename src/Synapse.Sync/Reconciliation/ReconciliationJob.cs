using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Synapse.Core.Ports;

namespace Synapse.Sync.Reconciliation;

/// <summary>
/// Executa reconciliação periódica de segurança comparando o disco local contra o índice SQLite (TECH-01, SRS 3.8).
/// Mitiga a perda de eventos do FileSystemWatcher sob carga alta sem sobrecarregar o sistema.
/// </summary>
public sealed class ReconciliationJob
{
    private readonly string _vaultRootPath;
    private readonly ISyncIndexStore _indexStore;
    private readonly IFileSystem _fileSystem;
    private readonly ChannelWriter<VaultChangeEvent> _eventWriter;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _timeProvider;
    private readonly Ignore.SynapseIgnoreMatcher _ignoreMatcher;
    private readonly ILogger<ReconciliationJob>? _logger;

    public ReconciliationJob(
        string vaultRootPath,
        ISyncIndexStore indexStore,
        IFileSystem fileSystem,
        ChannelWriter<VaultChangeEvent> eventWriter,
        TimeSpan? interval = null,
        TimeProvider? timeProvider = null,
        Ignore.SynapseIgnoreMatcher? ignoreMatcher = null,
        ILogger<ReconciliationJob>? logger = null)
    {
        _vaultRootPath = vaultRootPath ?? throw new ArgumentNullException(nameof(vaultRootPath));
        _indexStore = indexStore ?? throw new ArgumentNullException(nameof(indexStore));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _eventWriter = eventWriter ?? throw new ArgumentNullException(nameof(eventWriter));
        _interval = interval ?? TimeSpan.FromMinutes(15);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ignoreMatcher = ignoreMatcher ?? new Ignore.SynapseIgnoreMatcher();
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _logger?.LogInformation("ReconciliationJob iniciado com intervalo de {Interval} minutos.", _interval.TotalMinutes);

        using var timer = new PeriodicTimer(_interval, _timeProvider);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(ct);
                await ReconcileOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erro inesperado durante a execução do ReconciliationJob.");
            }
        }
    }

    public async Task<int> ReconcileOnceAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_vaultRootPath))
        {
            _logger?.LogWarning("Diretório do cofre não encontrado para reconciliação: {Path}", _vaultRootPath);
            return 0;
        }

        _logger?.LogDebug("Iniciando ciclo de reconciliação de segurança no cofre...");
        var divergencesFound = 0;

        try
        {
            var allFiles = Directory.EnumerateFiles(_vaultRootPath, "*.*", SearchOption.AllDirectories);

            foreach (var fullPath in allFiles)
            {
                if (ct.IsCancellationRequested) break;

                if (IsIgnoredPath(fullPath))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(_vaultRootPath, fullPath).Replace('\\', '/');

                string content;
                try
                {
                    content = await _fileSystem.ReadAllTextAsync(fullPath, ct);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Não foi possível ler arquivo durante reconciliação: {Path}", relativePath);
                    continue;
                }

                var currentHash = ContentHasher.Sha256(content);
                var record = await _indexStore.FindByLocalPathAsync(relativePath, ct);

                if (record == null)
                {
                    _logger?.LogInformation("Reconciliação: novo arquivo não indexado detectado: {Path}", relativePath);
                    await _eventWriter.WriteAsync(new VaultChangeEvent(relativePath, SyncEventType.Created), ct);
                    divergencesFound++;
                }
                else if (!string.Equals(record.ContentHash, currentHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogInformation("Reconciliação: alteração não registrada detectada: {Path}", relativePath);
                    await _eventWriter.WriteAsync(new VaultChangeEvent(relativePath, SyncEventType.Modified), ct);
                    divergencesFound++;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Falha durante varredura de reconciliação.");
        }

        _logger?.LogDebug("Ciclo de reconciliação concluído. Divergências encontradas: {Count}", divergencesFound);
        return divergencesFound;
    }

    private bool IsIgnoredPath(string fullPath)
    {
        var relative = Path.GetRelativePath(_vaultRootPath, fullPath).Replace('\\', '/');
        return _ignoreMatcher.ShouldIgnore(relative);
    }
}
