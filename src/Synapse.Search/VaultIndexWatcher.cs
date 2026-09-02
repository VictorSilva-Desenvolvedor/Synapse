using System.Collections.Concurrent;
using System.Text;

namespace Synapse.Search;

/// <summary>
/// Monitor de alterações de arquivos *.md sobre FileSystemWatcher com debounce thread-safe
/// e atualização incremental do índice SQLite FTS5.
/// </summary>
public sealed class VaultIndexWatcher : IVaultIndexWatcher
{
    private static readonly string[] IgnoredDirectories = [".obsidian", "_conflitos", ".trash"];

    private readonly IVaultSearchIndex _searchIndex;
    private readonly TimeSpan _debounceDelay;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounces;
    private readonly object _lock = new();

    private FileSystemWatcher? _watcher;
    private string? _vaultRootPath;
    private bool _isDisposed;

    public bool IsRunning => _watcher?.EnableRaisingEvents ?? false;
    public bool HasFailed { get; private set; }
    public event EventHandler<Exception>? ErrorOccurred;

    public VaultIndexWatcher(
        IVaultSearchIndex searchIndex,
        TimeSpan? debounceDelay = null)
    {
        _searchIndex = searchIndex ?? throw new ArgumentNullException(nameof(searchIndex));
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(500);
        _debounces = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
    }

    public void Start(string vaultRootPath)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath))
        {
            throw new ArgumentException("Vault root path cannot be null or whitespace.", nameof(vaultRootPath));
        }

        if (!Directory.Exists(vaultRootPath))
        {
            throw new DirectoryNotFoundException($"Vault root directory does not exist: '{vaultRootPath}'");
        }

        lock (_lock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(VaultIndexWatcher));
            }

            if (_watcher != null)
            {
                Stop();
            }

            HasFailed = false;
            _vaultRootPath = Path.GetFullPath(vaultRootPath);

            _watcher = new FileSystemWatcher(_vaultRootPath)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
                Filter = "*.md"
            };

            _watcher.Created += OnFileSystemEvent;
            _watcher.Changed += OnFileSystemEvent;
            _watcher.Deleted += OnFileSystemDeleted;
            _watcher.Renamed += OnFileSystemRenamed;
            _watcher.Error += OnWatcherError;

            _watcher.EnableRaisingEvents = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileSystemEvent;
                _watcher.Changed -= OnFileSystemEvent;
                _watcher.Deleted -= OnFileSystemDeleted;
                _watcher.Renamed -= OnFileSystemRenamed;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
                _watcher = null;
            }

            _vaultRootPath = null;
            CancelAllPendingDebounces();
        }
    }

    private void CancelAllPendingDebounces()
    {
        foreach (var kvp in _debounces)
        {
            if (_debounces.TryRemove(kvp.Key, out var cts))
            {
                try
                {
                    cts.Cancel();
                }
                catch { }
                try
                {
                    cts.Dispose();
                }
                catch { }
            }
        }
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (_vaultRootPath == null || !IsWatchedMarkdownFile(e.FullPath))
        {
            return;
        }

        var canonicalPath = HybridSearchEngine.ToCanonicalRelativePath(e.FullPath, _vaultRootPath);
        if (string.IsNullOrEmpty(canonicalPath) || IsIgnoredPath(canonicalPath))
        {
            return;
        }

        ScheduleDebounce(e.FullPath, canonicalPath);
    }

    private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
    {
        if (_vaultRootPath == null)
        {
            return;
        }

        var canonicalPath = HybridSearchEngine.ToCanonicalRelativePath(e.FullPath, _vaultRootPath);
        if (string.IsNullOrEmpty(canonicalPath) || IsIgnoredPath(canonicalPath))
        {
            return;
        }

        // Cancela qualquer debounce pendente para esse arquivo
        if (_debounces.TryRemove(canonicalPath, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch { }
            try
            {
                cts.Dispose();
            }
            catch { }
        }

        // Remove do índice de forma assíncrona segura
        _ = Task.Run(async () =>
        {
            try
            {
                await _searchIndex.RemoveFileAsync(canonicalPath).ConfigureAwait(false);
            }
            catch
            {
                // Erro isolado não quebra o watcher
            }
        });
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        if (_vaultRootPath == null)
        {
            return;
        }

        var oldCanonical = HybridSearchEngine.ToCanonicalRelativePath(e.OldFullPath, _vaultRootPath);
        if (!string.IsNullOrEmpty(oldCanonical) && !IsIgnoredPath(oldCanonical))
        {
            if (_debounces.TryRemove(oldCanonical, out var oldCts))
            {
                try
                {
                    oldCts.Cancel();
                }
                catch { }
                try
                {
                    oldCts.Dispose();
                }
                catch { }
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _searchIndex.RemoveFileAsync(oldCanonical).ConfigureAwait(false);
                }
                catch { }
            });
        }

        if (IsWatchedMarkdownFile(e.FullPath))
        {
            var newCanonical = HybridSearchEngine.ToCanonicalRelativePath(e.FullPath, _vaultRootPath);
            if (!string.IsNullOrEmpty(newCanonical) && !IsIgnoredPath(newCanonical))
            {
                ScheduleDebounce(e.FullPath, newCanonical);
            }
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        HasFailed = true;
        var ex = e.GetException();
        ErrorOccurred?.Invoke(this, ex);
    }

    internal void SimulateWatcherError(Exception? ex = null)
    {
        OnWatcherError(this, new ErrorEventArgs(ex ?? new IOException("Erro simulado no buffer do FileSystemWatcher.")));
    }

    private void ScheduleDebounce(string fullPath, string canonicalPath)
    {
        var cts = new CancellationTokenSource();

        // Substitui e cancela o timer anterior para a mesma chave canônica
        if (_debounces.TryGetValue(canonicalPath, out var existingCts))
        {
            try
            {
                existingCts.Cancel();
            }
            catch { }
            try
            {
                existingCts.Dispose();
            }
            catch { }
        }

        _debounces[canonicalPath] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounceDelay, cts.Token).ConfigureAwait(false);

                if (cts.Token.IsCancellationRequested)
                {
                    return;
                }

                if (File.Exists(fullPath))
                {
                    string? content = await ReadFileWithRetryAsync(fullPath, cts.Token).ConfigureAwait(false);
                    if (content != null && !cts.Token.IsCancellationRequested)
                    {
                        await _searchIndex.IndexFileAsync(canonicalPath, content, cts.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    await _searchIndex.RemoveFileAsync(canonicalPath, cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Esperado no cancelamento
            }
            catch
            {
                // Erros de I/O transitórios ou isolados não derrubam o watcher
            }
            finally
            {
                if (_debounces.TryGetValue(canonicalPath, out var current) && current == cts)
                {
                    _debounces.TryRemove(canonicalPath, out _);
                }

                cts.Dispose();
            }
        });
    }

    private static async Task<string?> ReadFileWithRetryAsync(string fullPath, CancellationToken ct)
    {
        const int maxRetries = 5;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }

        return null;
    }

    private static bool IsWatchedMarkdownFile(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    private static bool IsIgnoredPath(string relativePath)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(s => s.StartsWith('.') || IgnoredDirectories.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            Stop();
        }
    }
}
