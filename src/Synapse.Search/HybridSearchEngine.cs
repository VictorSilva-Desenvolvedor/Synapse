using System.Runtime.CompilerServices;

namespace Synapse.Search;

/// <summary>
/// Despachante de busca híbrido do Synapse:
/// - isRegex: true -> Ripgrep sozinho (suporte nativo a expressões regulares).
/// - Texto comum + índice pronto -> SQLite FTS5 sozinho (<50 ms, sem I/O linear no disco).
/// - Texto comum + índice não pronto -> Ripgrep como contingência (enquanto indexa ou se o watcher falhar).
///
/// Os dois motores nunca mais rodam juntos.
/// </summary>
public sealed class HybridSearchEngine : IHybridSearchEngine
{
    private const double RrfK = 60.0;

    private readonly IVaultSearchIndex _searchIndex;
    private readonly IRawSearchEngine _rawSearchEngine;
    private readonly Func<bool>? _isIndexReadyFunc;

    public Func<bool>? IsIndexReady { get; set; }

    public HybridSearchEngine(
        IVaultSearchIndex searchIndex,
        IRawSearchEngine rawSearchEngine,
        Func<bool>? isIndexReady = null)
    {
        _searchIndex = searchIndex ?? throw new ArgumentNullException(nameof(searchIndex));
        _rawSearchEngine = rawSearchEngine ?? throw new ArgumentNullException(nameof(rawSearchEngine));
        _isIndexReadyFunc = isIndexReady;
    }

    public async IAsyncEnumerable<HybridSearchResult> SearchAsync(
        string vaultRootPath,
        string query,
        bool isRegex = false,
        int limit = 100,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath))
        {
            throw new ArgumentException("Vault root path cannot be null or whitespace.", nameof(vaultRootPath));
        }

        if (!Directory.Exists(vaultRootPath))
        {
            throw new DirectoryNotFoundException($"Vault root directory does not exist: '{vaultRootPath}'");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            yield break;
        }

        if (limit <= 0)
        {
            limit = 100;
        }

        ct.ThrowIfCancellationRequested();

        bool isReady = IsIndexReady?.Invoke() ?? _isIndexReadyFunc?.Invoke() ?? true;

        // Despachante: os dois motores nunca mais rodam juntos.
        // 1. isRegex: true -> Ripgrep sozinho.
        // 2. Texto comum + índice pronto -> FTS5 sozinho (<50ms).
        // 3. Texto comum + índice não pronto -> Ripgrep como contingência.
        if (isRegex || !isReady)
        {
            await foreach (var result in ExecuteRipgrepOnlyAsync(vaultRootPath, query, isRegex, limit, ct).ConfigureAwait(false))
            {
                yield return result;
            }
        }
        else
        {
            await foreach (var result in ExecuteFtsOnlyAsync(vaultRootPath, query, limit, ct).ConfigureAwait(false))
            {
                yield return result;
            }
        }
    }

    private async IAsyncEnumerable<HybridSearchResult> ExecuteFtsOnlyAsync(
        string vaultRootPath,
        string query,
        int limit,
        [EnumeratorCancellation] CancellationToken ct)
    {
        int rank = 1;
        int yielded = 0;

        await foreach (var item in _searchIndex.SearchAsync(query, limit * 2, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            var canonical = ToCanonicalRelativePath(item.FilePath, vaultRootPath);
            if (string.IsNullOrEmpty(canonical))
            {
                continue;
            }

            // Descarte de arquivos fantasmas (deletados do disco físico mas presentes no índice)
            var fullPhysicalPath = Path.Combine(vaultRootPath, canonical.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPhysicalPath))
            {
                continue;
            }

            yield return new HybridSearchResult(
                FilePath: canonical,
                Score: 1.0 / (RrfK + rank++),
                Source: SearchMatchSource.IndexOnly,
                Snippet: item.Snippet,
                RipgrepMatches: Array.Empty<RipgrepMatch>());

            if (++yielded >= limit)
            {
                break;
            }
        }
    }

    private async IAsyncEnumerable<HybridSearchResult> ExecuteRipgrepOnlyAsync(
        string vaultRootPath,
        string query,
        bool isRegex,
        int limit,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var dict = new Dictionary<string, (int Rank, List<RipgrepMatch> Matches)>(StringComparer.OrdinalIgnoreCase);
        var orderedFiles = new List<string>();
        int currentRank = 1;

        await foreach (var match in _rawSearchEngine.SearchAsync(vaultRootPath, query, isRegex, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            var canonical = ToCanonicalRelativePath(match.FilePath, vaultRootPath);
            if (string.IsNullOrEmpty(canonical))
            {
                continue;
            }

            if (!dict.TryGetValue(canonical, out var entry))
            {
                entry = (currentRank++, new List<RipgrepMatch>());
                dict[canonical] = entry;
                orderedFiles.Add(canonical);
            }

            entry.Matches.Add(match);
        }

        int yielded = 0;
        foreach (var canonical in orderedFiles)
        {
            ct.ThrowIfCancellationRequested();

            var (rank, matches) = dict[canonical];
            var snippet = matches.Count > 0 ? matches[0].LineText : null;

            yield return new HybridSearchResult(
                FilePath: canonical,
                Score: 1.0 / (RrfK + rank),
                Source: SearchMatchSource.RipgrepOnly,
                Snippet: snippet,
                RipgrepMatches: matches);

            if (++yielded >= limit)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Normaliza qualquer caminho (absoluto, relativo, com barras invertidas ou normais)
    /// para o formato canônico relativo à raiz do cofre com separadores '/'.
    /// </summary>
    public static string ToCanonicalRelativePath(string rawPath, string vaultRootPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        string relative;
        try
        {
            if (Path.IsPathRooted(rawPath))
            {
                relative = Path.GetRelativePath(vaultRootPath, rawPath);
            }
            else
            {
                var combined = Path.GetFullPath(Path.Combine(vaultRootPath, rawPath));
                relative = Path.GetRelativePath(vaultRootPath, combined);
            }
        }
        catch
        {
            relative = rawPath;
        }

        return relative.Replace('\\', '/').TrimStart('/');
    }
}
