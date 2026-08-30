namespace Synapse.Sync.Diagnostics;

/// <summary>
/// As tres versoes de uma nota em conflito, prontas para o merge de 3 vias.
/// <paramref name="BasePath"/> e nulo quando o cache de base nao tem a nota — o que
/// acontece de verdade quando o conflito surge no primeiro sync dela.
/// </summary>
public sealed record ConflictSources(
    string TargetRelativePath,
    string LocalPath,
    string RemotePath,
    string? BasePath);

/// <summary>
/// Localiza as tres versoes de uma nota em conflito a partir de qualquer arquivo dentro
/// da pasta de conflito.
///
/// O <see cref="Synapse.Sync.SyncQueueProcessor"/> grava conflitos como
/// <c>_conflitos/{caminho/da/nota.md}/local-{timestamp}.md</c> e
/// <c>remoto-{timestamp}.md</c> — ou seja, o "caminho" e um DIRETORIO com o nome da nota
/// (incluindo a extensao), contendo as duas versoes. A base fica fora do cofre, no cache
/// que o SyncBaseCache mantem.
/// </summary>
public static class ConflictSetResolver
{
    public const string ConflictsFolderName = "_conflitos";

    /// <summary>Mesma raiz que o Synapse.Host passa ao SyncQueueProcessor.</summary>
    public static string DefaultBaseCacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Synapse",
        "base_cache");

    /// <summary>
    /// Resolve o trio a partir de um arquivo qualquer dentro da pasta de conflito —
    /// tanto faz se o usuario clicou no local-*.md ou no remoto-*.md.
    /// Devolve null se o caminho nao estiver sob <c>_conflitos/</c>.
    /// </summary>
    public static ConflictSources? Resolve(
        string vaultRootPath,
        string anyConflictFilePath,
        string? baseCacheRoot = null)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath) || string.IsNullOrWhiteSpace(anyConflictFilePath))
        {
            return null;
        }

        var conflictDir = Path.GetDirectoryName(anyConflictFilePath);
        if (string.IsNullOrEmpty(conflictDir))
        {
            return null;
        }

        var conflictsRoot = Path.Combine(vaultRootPath, ConflictsFolderName);
        var relative = Path.GetRelativePath(conflictsRoot, conflictDir).Replace('\\', '/');

        // Fora de _conflitos/: GetRelativePath devolve algo comecando com ".." ou o
        // proprio caminho absoluto.
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return null;
        }

        var localPath = NewestMatching(conflictDir, "local-*");
        var remotePath = NewestMatching(conflictDir, "remoto-*");

        if (localPath is null || remotePath is null)
        {
            return null;
        }

        var basePath = Path.Combine(baseCacheRoot ?? DefaultBaseCacheRoot, relative.Replace('/', Path.DirectorySeparatorChar));

        return new ConflictSources(
            relative,
            localPath,
            remotePath,
            File.Exists(basePath) ? basePath : null);
    }

    /// <summary>
    /// O mais recente do padrao. Um mesmo conflito pode ter varias rodadas preservadas
    /// no mesmo diretorio; a ultima e a que interessa resolver.
    /// </summary>
    private static string? NewestMatching(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return new DirectoryInfo(directory)
            .GetFiles(pattern)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .FirstOrDefault();
    }
}
