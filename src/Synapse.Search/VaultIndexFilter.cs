namespace Synapse.Search;

/// <summary>
/// Regra unica de exclusao de arquivos do indice de busca, compartilhada pelo VaultBulkIndexer e
/// pelo VaultIndexWatcher. Ficar em um lugar so e proposital: nas fases anteriores o bulk e o
/// watcher divergiram (chave absoluta vs relativa) e o resultado foi indice inconsistente sem
/// nenhum teste vermelho. Aqui o risco era o mesmo - o watcher ja ignorava .obsidian/_conflitos/
/// .trash e o bulk nao ignorava nada.
/// </summary>
public static class VaultIndexFilter
{
    private static readonly string[] IgnoredSegments = [".obsidian", "_conflitos", ".trash", ".synapse"];

    /// <summary>
    /// Pastas que o proprio Synapse escreve dentro do cofre. Os logs de atividade sao o caso critico:
    /// eles registram o texto literal de toda pergunta ja feita ao chat, entao casam com qualquer
    /// consulta do usuario e afogam as notas de verdade. Verificado no cofre real - perguntar
    /// "me diga minha lista de amigos" devolvia so os registros de atividade, e a nota certa
    /// ("Brain/Pessoas/Lista de Amigos.md") nem aparecia. Sao telemetria, nao conhecimento.
    /// </summary>
    private static readonly string[] IgnoredPathPrefixes = ["Synapse/Logs/"];

    /// <summary>
    /// Decide se um caminho relativo canonico (separadores '/', relativo a raiz do cofre)
    /// deve ficar de fora do indice de busca.
    /// </summary>
    public static bool ShouldIgnore(string canonicalRelativePath)
    {
        if (string.IsNullOrWhiteSpace(canonicalRelativePath))
        {
            return true;
        }

        foreach (var prefix in IgnoredPathPrefixes)
        {
            if (canonicalRelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var segments = canonicalRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment.StartsWith('.') ||
                IgnoredSegments.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
