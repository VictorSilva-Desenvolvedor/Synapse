using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace Synapse.Search;

/// <summary>
/// Read-only mirror of the vault in a SQLite FTS5 table. It never writes to the .md
/// files and can be deleted and rebuilt at any time.
///
/// Every operation opens its own connection instead of sharing one. SqliteConnection is
/// not thread-safe, and the intended usage runs a background bulk index at the same time
/// as searches from the UI - on a shared connection those two collide. Serializing them
/// behind a lock would fix the collision but make the UI wait for a 500-file transaction
/// to commit, which defeats the point. Separate connections plus WAL let readers run
/// while a writer holds the write lock.
/// </summary>
public sealed class SqliteVaultSearchIndex : IVaultSearchIndex
{
    private readonly string _connectionString;

    public SqliteVaultSearchIndex(string connectionString)
    {
        _connectionString = connectionString;

        using var connection = OpenConnection();
        EnsureSchema(connection);
    }

    /// <summary>
    /// Builds an index over a database file, creating the directory if it is missing.
    ///
    /// SqliteSyncIndexStore.ForFile leaves that to the caller, and it only works today
    /// because something else creates %LOCALAPPDATA%\Synapse first. This index is meant
    /// to live in its own subdirectory, where nothing guarantees that - and the failure
    /// is a SqliteException at startup, not a missing file that gets created.
    ///
    /// Pooling=false because the pool keeps the file handle alive after the connection
    /// closes, which blocks deleting or rebuilding the index.
    /// </summary>
    public static SqliteVaultSearchIndex ForFile(string databaseFilePath)
    {
        var directory = Path.GetDirectoryName(databaseFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new(new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            Pooling = false
        }.ToString());
    }

    /// <summary>
    /// WAL is what allows a search to read while the bulk indexer is writing; in the
    /// default rollback journal the writer blocks every reader for the whole transaction.
    /// busy_timeout covers the remaining case of two writers meeting, where SQLite would
    /// otherwise fail immediately with SQLITE_BUSY instead of waiting its turn.
    /// </summary>
    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;
            """;
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS VaultIndex USING fts5(
                file_path UNINDEXED,
                content,
                tokenize='unicode61 remove_diacritics 2'
            );
            """;
        command.ExecuteNonQuery();
    }

    public Task IndexFileAsync(string filePath, string content, CancellationToken ct = default) =>
        IndexBatchAsync(new[] { (filePath, content) }, ct);

    public async Task IndexBatchAsync(IEnumerable<(string FilePath, string Content)> batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ct.ThrowIfCancellationRequested();

        var items = batch as IList<(string FilePath, string Content)> ?? batch.ToList();
        if (items.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var deleteCmd = connection.CreateCommand();
        deleteCmd.Transaction = transaction;
        deleteCmd.CommandText = "DELETE FROM VaultIndex WHERE file_path = $filePath;";
        var delParam = deleteCmd.CreateParameter();
        delParam.ParameterName = "$filePath";
        deleteCmd.Parameters.Add(delParam);

        using var insertCmd = connection.CreateCommand();
        insertCmd.Transaction = transaction;
        insertCmd.CommandText = "INSERT INTO VaultIndex (file_path, content) VALUES ($filePath, $content);";
        var insPath = insertCmd.CreateParameter();
        insPath.ParameterName = "$filePath";
        insertCmd.Parameters.Add(insPath);
        var insContent = insertCmd.CreateParameter();
        insContent.ParameterName = "$content";
        insertCmd.Parameters.Add(insContent);

        foreach (var (filePath, content) in items)
        {
            ct.ThrowIfCancellationRequested();

            delParam.Value = filePath;
            await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            insPath.Value = filePath;
            insContent.Value = content;
            await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveFileAsync(string filePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM VaultIndex WHERE file_path = $filePath;";
        cmd.Parameters.AddWithValue("$filePath", filePath);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Palavras vazias do portugues, removidas de perguntas em linguagem natural antes de montar a
    /// consulta FTS5. Sem isso, "me diga minha lista de amigos" exigia que a nota contivesse tambem
    /// "me", "diga" e "minha" - e a nota certa do usuario ("Brain/Pessoas/Lista de Amigos.md") era
    /// excluida, enquanto os registros de atividade (que citam a pergunta inteira) casavam. Medido
    /// no cofre real: a mesma pergunta sem as palavras vazias traz a nota certa no topo.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "as", "ao", "aos", "o", "os", "um", "uma", "uns", "umas",
        "de", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas",
        "por", "para", "pra", "com", "sem", "sobre", "entre",
        "e", "ou", "que", "se", "qual", "quais", "quando", "onde", "como", "quem",
        "meu", "minha", "meus", "minhas", "seu", "sua", "seus", "suas",
        "me", "mim", "eu", "voce", "ele", "ela", "isso", "este", "esta", "esse", "essa",
        "diga", "diz", "fale", "mostre", "liste", "quero", "gostaria", "preciso",
        "ser", "e", "sao", "esta", "estao", "foi", "tem", "ter", "há", "ha",
        "the", "of", "in", "to", "is", "are", "my", "me", "show", "list", "tell"
    };

    /// <summary>
    /// Monta a consulta FTS5 a partir do texto do usuario.
    /// Cada termo vai entre aspas para que nada (AND, OR, NOT, *, :, parenteses) seja interpretado
    /// como operador. Poucos termos ficam em E implicito, que e mais preciso; muitos termos passam a
    /// OU explicito, porque exigir todas as palavras de uma frase longa elimina justamente o
    /// documento certo - o bm25 se encarrega de ranquear na frente quem contem mais termos.
    /// </summary>
    internal static string BuildFtsQuery(string query)
    {
        var rawTerms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // Se sobrar termo de conteudo, usa so eles; se a consulta for feita apenas de palavras
        // vazias (ex.: "o que e isso"), mantem os originais para nao buscar por nada.
        var contentTerms = rawTerms.Where(t => !StopWords.Contains(t.Trim())).ToArray();
        var terms = contentTerms.Length > 0 ? contentTerms : rawTerms;

        var quoted = terms.Select(term => $"\"{term.Replace("\"", "\"\"")}\"").ToArray();

        // Ate 3 termos: E implicito (espaco entre frases no FTS5). Acima disso, OU.
        return quoted.Length <= 3
            ? string.Join(' ', quoted)
            : string.Join(" OR ", quoted);
    }

    public async IAsyncEnumerable<VaultSearchResult> SearchAsync(
        string query,
        int limit = 100,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            yield break;
        }

        if (limit <= 0)
        {
            limit = 100;
        }

        var ftsLiteralQuery = BuildFtsQuery(query);

        // The connection lives as long as the enumeration: disposing it here closes it
        // when the caller stops reading, including an early break.
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT file_path,
                   snippet(VaultIndex, 1, '<b>', '</b>', '...', 32) AS snippet_text,
                   bm25(VaultIndex) AS rank_score
            FROM VaultIndex
            WHERE VaultIndex MATCH $query
            ORDER BY rank_score
            LIMIT $limit;
            """;

        cmd.Parameters.AddWithValue("$query", ftsLiteralQuery);
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var filePath = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var snippet = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var rank = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2);

            yield return new VaultSearchResult(filePath, snippet, rank);
        }
    }

    public async Task<int> GetIndexedFileCountAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM VaultIndex;";
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Nothing to release: no connection outlives the operation that opened it. Kept
    /// because IVaultSearchIndex is IDisposable and an implementation may need it.
    /// </summary>
    public void Dispose()
    {
    }
}
