namespace Synapse.Search;

/// <summary>
/// Identifica a origem de cada resultado retornado pela busca híbrida.
/// </summary>
public enum SearchMatchSource
{
    /// <summary>
    /// O termo foi encontrado apenas no índice SQLite FTS5 (ex: arquivo foi editado
    /// no disco removendo o termo, ou a consulta casa por E-de-termos tokenizado em
    /// posições separadas do documento).
    /// </summary>
    IndexOnly,

    /// <summary>
    /// O termo foi encontrado apenas pelo Ripgrep diretamente nos arquivos vivos
    /// (ex: arquivo novo ou modificado recentemente após a última indexação em lote).
    /// </summary>
    RipgrepOnly,

    /// <summary>
    /// O termo foi encontrado tanto no índice FTS5 quanto pelo Ripgrep no disco vivo.
    /// </summary>
    Both
}
