using System.Collections.Concurrent;

namespace Synapse.Sync;

/// <summary>
/// Rastreia arquivos gravados recentemente pelo próprio mecanismo de sincronização (downloads ou merge de conflitos)
/// para suprimir eventos de eco gerados pelo FileSystemWatcher local.
/// </summary>
public sealed class RecentSelfWriteTracker
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentWrites = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _window;
    private readonly TimeProvider _timeProvider;

    public RecentSelfWriteTracker(TimeSpan? window = null, TimeProvider? timeProvider = null)
    {
        _window = window ?? TimeSpan.FromSeconds(3);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Registra que um arquivo foi gravado localmente pelo Synapse.
    /// </summary>
    public void MarkWritten(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        var normalized = Normalize(relativePath);
        _recentWrites[normalized] = _timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Retorna verdadeiro se o arquivo foi gravado pelo Synapse dentro da janela recente (~3s).
    /// </summary>
    public bool WasRecentlyWrittenByUs(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;
        var normalized = Normalize(relativePath);

        if (_recentWrites.TryGetValue(normalized, out var timestamp))
        {
            var elapsed = _timeProvider.GetUtcNow() - timestamp;
            if (elapsed >= TimeSpan.Zero && elapsed <= _window)
            {
                return true;
            }

            _recentWrites.TryRemove(normalized, out _);
        }

        return false;
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/').Trim();
}
