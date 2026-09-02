namespace Synapse.Search;

/// <summary>
/// Resultado unificado da busca híbrida, fundido via Reciprocal Rank Fusion (RRF k=60).
/// </summary>
/// <param name="FilePath">Caminho relativo canônico do arquivo em relação à raiz do cofre.</param>
/// <param name="Score">Pontuação calculada via RRF (quanto maior, mais relevante).</param>
/// <param name="Source">Origem do match (IndexOnly, RipgrepOnly ou Both).</param>
/// <param name="Snippet">Trecho contextual destacado do termo.</param>
/// <param name="RipgrepMatches">Ocorrências pontuais com números de linha e offsets retornados pelo Ripgrep.</param>
public sealed record HybridSearchResult(
    string FilePath,
    double Score,
    SearchMatchSource Source,
    string? Snippet,
    IReadOnlyList<RipgrepMatch> RipgrepMatches);
