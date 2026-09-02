namespace Synapse.Search;

public sealed class VaultBulkIndexer : IVaultBulkIndexer
{
    private readonly int _batchSize;

    public VaultBulkIndexer(int batchSize = 500)
    {
        _batchSize = batchSize > 0 ? batchSize : 500;
    }

    public async Task<int> IndexVaultAsync(
        string vaultRootPath,
        IVaultSearchIndex searchIndex,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath))
        {
            throw new ArgumentException("Vault root path cannot be null or whitespace.", nameof(vaultRootPath));
        }

        if (!Directory.Exists(vaultRootPath))
        {
            throw new DirectoryNotFoundException($"Vault root directory does not exist: '{vaultRootPath}'");
        }

        ArgumentNullException.ThrowIfNull(searchIndex);

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        var mdFiles = Directory.EnumerateFiles(vaultRootPath, "*.md", enumerationOptions);

        int totalIndexed = 0;
        var batch = new List<(string FilePath, string Content)>(_batchSize);

        foreach (var filePath in mdFiles)
        {
            ct.ThrowIfCancellationRequested();

            // Chave canonica relativa ao cofre, a MESMA forma usada pelo VaultIndexWatcher.
            // Se o bulk gravasse o caminho absoluto, um arquivo indexado aqui e depois editado
            // ganharia uma segunda linha no indice (sob a chave relativa do watcher) e a linha
            // antiga sobreviveria para sempre - um termo apagado pelo usuario continuaria sendo
            // encontrado na busca. Provado por
            // HybridSearchServiceTests.EditingFileIndexedByBulk_DoesNotLeaveStaleContentSearchable.
            var canonicalPath = HybridSearchEngine.ToCanonicalRelativePath(filePath, vaultRootPath);

            // Mesma regra de exclusao do VaultIndexWatcher (VaultIndexFilter), aplicada antes de
            // ler o arquivo: sem isso o bulk indexava os logs de atividade do proprio Synapse, que
            // contem o texto de toda pergunta ja feita e por isso casam com qualquer consulta.
            if (VaultIndexFilter.ShouldIgnore(canonicalPath))
            {
                continue;
            }

            string content;
            try
            {
                content = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Skip files currently locked or inaccessible
                continue;
            }

            batch.Add((canonicalPath, content));

            if (batch.Count >= _batchSize)
            {
                await searchIndex.IndexBatchAsync(batch, ct).ConfigureAwait(false);
                totalIndexed += batch.Count;
                progress?.Report(totalIndexed);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            await searchIndex.IndexBatchAsync(batch, ct).ConfigureAwait(false);
            totalIndexed += batch.Count;
            progress?.Report(totalIndexed);
            batch.Clear();
        }

        return totalIndexed;
    }
}
