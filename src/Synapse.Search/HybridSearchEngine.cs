using System.Runtime.CompilerServices;

namespace Synapse.Search;

/// <summary>
/// Motor de busca híbrido que combina a busca direta e exata em disco do Ripgrep
/// com a busca indexada em SQLite FTS5 via Reciprocal Rank Fusion (RRF k=60).
///
/// Semântica e divergência entre motores:
/// - Ripgrep busca por substring exata ou regex diretamente nos arquivos vivos no disco.
/// - SQLite FTS5 busca por E-de-termos tokenizados (com remoção de acentos via remove_diacritics 2),
///   onde os termos podem aparecer em posições arbitrárias dentro do documento.
/// - Documentos contendo a frase exata ou encontrados em ambos os motores recebem a soma
///   das pontuações recíprocas (SearchMatchSource.Both), assumindo o topo do ranking.
/// - Documentos onde os termos estão espalhados pelo texto aparecem como SearchMatchSource.IndexOnly.
/// - Arquivos criados ou modificados recentemente após a última indexação em lote aparecem
///   como SearchMatchSource.RipgrepOnly.
/// - Arquivos deletados fisicamente do disco que ainda constam no índice defasado são
///   automaticamente descartados na consolidação final.
/// </summary>
public sealed class HybridSearchEngine : IHybridSearchEngine
{
    private const double RrfK = 60.0;

    private readonly IVaultSearchIndex _searchIndex;
    private readonly IRawSearchEngine _rawSearchEngine;

    public HybridSearchEngine(
        IVaultSearchIndex searchIndex,
        IRawSearchEngine rawSearchEngine)
    {
        _searchIndex = searchIndex ?? throw new ArgumentNullException(nameof(searchIndex));
        _rawSearchEngine = rawSearchEngine ?? throw new ArgumentNullException(nameof(rawSearchEngine));
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

        // 1. Executa ambas as buscas concorrentemente
        var ftsTask = CollectFtsResultsAsync(query, limit * 3, ct);
        var rgTask = CollectRipgrepResultsAsync(vaultRootPath, query, isRegex, ct);

        await Task.WhenAll(ftsTask, rgTask).ConfigureAwait(false);

        var ftsResults = await ftsTask.ConfigureAwait(false);
        var rgResults = await rgTask.ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        // 2. Mapeamento de rankings 1-indexed por caminho canônico
        // FTS Ranking: preserva a ordem BM25 do SQLite
        var ftsRanking = new Dictionary<string, (int Rank, VaultSearchResult Item)>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ftsResults.Count; i++)
        {
            var item = ftsResults[i];
            var canonical = ToCanonicalRelativePath(item.FilePath, vaultRootPath);
            if (!string.IsNullOrEmpty(canonical) && !ftsRanking.ContainsKey(canonical))
            {
                ftsRanking[canonical] = (i + 1, item);
            }
        }

        // Ripgrep Ranking: preserva a ordem do primeiro match retornado por arquivo
        var rgRanking = new Dictionary<string, (int Rank, List<RipgrepMatch> Matches)>(StringComparer.OrdinalIgnoreCase);
        int currentRgRank = 1;
        foreach (var (canonicalPath, matches) in rgResults)
        {
            if (!rgRanking.ContainsKey(canonicalPath))
            {
                rgRanking[canonicalPath] = (currentRgRank++, matches);
            }
        }

        // 3. Conjunto unificado de todos os caminhos encontrados
        var allPaths = new HashSet<string>(ftsRanking.Keys, StringComparer.OrdinalIgnoreCase);
        allPaths.UnionWith(rgRanking.Keys);

        // 4. Reciprocal Rank Fusion (RRF k=60)
        var fusedResults = new List<HybridSearchResult>(allPaths.Count);

        foreach (var path in allPaths)
        {
            ct.ThrowIfCancellationRequested();

            bool hasFts = ftsRanking.TryGetValue(path, out var ftsEntry);
            bool hasRg = rgRanking.TryGetValue(path, out var rgEntry);

            // Descarte de arquivos fantasmas (deletados do disco físico mas presentes no índice)
            var fullPhysicalPath = Path.Combine(vaultRootPath, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPhysicalPath))
            {
                continue;
            }

            double ftsScore = hasFts ? 1.0 / (RrfK + ftsEntry.Rank) : 0.0;
            double rgScore = hasRg ? 1.0 / (RrfK + rgEntry.Rank) : 0.0;
            double totalScore = ftsScore + rgScore;

            SearchMatchSource source;
            if (hasFts && hasRg)
            {
                source = SearchMatchSource.Both;
            }
            else if (hasFts)
            {
                source = SearchMatchSource.IndexOnly;
            }
            else
            {
                source = SearchMatchSource.RipgrepOnly;
            }

            string? snippet = null;
            if (hasFts && !string.IsNullOrWhiteSpace(ftsEntry.Item.Snippet))
            {
                snippet = ftsEntry.Item.Snippet;
            }
            else if (hasRg && rgEntry.Matches.Count > 0)
            {
                snippet = rgEntry.Matches[0].LineText;
            }

            var matchesList = hasRg ? (IReadOnlyList<RipgrepMatch>)rgEntry.Matches : Array.Empty<RipgrepMatch>();

            fusedResults.Add(new HybridSearchResult(
                FilePath: path,
                Score: totalScore,
                Source: source,
                Snippet: snippet,
                RipgrepMatches: matchesList));
        }

        // 5. Ordenação decrescente por score RRF com desempate determinístico por caminho
        var orderedResults = fusedResults
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(limit);

        foreach (var result in orderedResults)
        {
            ct.ThrowIfCancellationRequested();
            yield return result;
        }
    }

    private async Task<List<VaultSearchResult>> CollectFtsResultsAsync(
        string query,
        int limit,
        CancellationToken ct)
    {
        var list = new List<VaultSearchResult>();
        try
        {
            await foreach (var item in _searchIndex.SearchAsync(query, limit, ct).ConfigureAwait(false))
            {
                list.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Erros no FTS não devem derrubar a busca se o Ripgrep puder responder
        }

        return list;
    }

    private async Task<List<(string CanonicalPath, List<RipgrepMatch> Matches)>> CollectRipgrepResultsAsync(
        string vaultRootPath,
        string query,
        bool isRegex,
        CancellationToken ct)
    {
        var dict = new Dictionary<string, List<RipgrepMatch>>(StringComparer.OrdinalIgnoreCase);
        var orderedList = new List<(string CanonicalPath, List<RipgrepMatch> Matches)>();

        try
        {
            await foreach (var match in _rawSearchEngine.SearchAsync(vaultRootPath, query, isRegex, ct).ConfigureAwait(false))
            {
                var canonical = ToCanonicalRelativePath(match.FilePath, vaultRootPath);
                if (string.IsNullOrEmpty(canonical))
                {
                    continue;
                }

                if (!dict.TryGetValue(canonical, out var list))
                {
                    list = new List<RipgrepMatch>();
                    dict[canonical] = list;
                    orderedList.Add((canonical, list));
                }

                list.Add(match);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Erros no Ripgrep não devem derrubar a busca se o FTS puder responder
        }

        return orderedList;
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
