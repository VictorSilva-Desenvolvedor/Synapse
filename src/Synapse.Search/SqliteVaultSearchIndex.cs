using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace Synapse.Search;

public sealed class SqliteVaultSearchIndex : IVaultSearchIndex
{
    private readonly SqliteConnection _connection;

    public SqliteVaultSearchIndex(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        EnsureSchema();
    }

    public static SqliteVaultSearchIndex ForFile(string databaseFilePath) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            Pooling = false
        }.ToString());

    private void EnsureSchema()
    {
        using var command = _connection.CreateCommand();
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

        using var transaction = _connection.BeginTransaction();

        using var deleteCmd = _connection.CreateCommand();
        deleteCmd.Transaction = transaction;
        deleteCmd.CommandText = "DELETE FROM VaultIndex WHERE file_path = $filePath;";
        var delParam = deleteCmd.CreateParameter();
        delParam.ParameterName = "$filePath";
        deleteCmd.Parameters.Add(delParam);

        using var insertCmd = _connection.CreateCommand();
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

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM VaultIndex WHERE file_path = $filePath;";
        cmd.Parameters.AddWithValue("$filePath", filePath);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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

        // Each term is quoted separately, not the whole query as one string. Quoting the
        // whole query also made it a single FTS5 phrase, so "sync conflict" only matched
        // documents where those words were adjacent - a note containing both words apart
        // returned nothing. Quoting per term keeps every character literal (AND, OR, NOT,
        // *, :, ( ) are never parsed as operators) while restoring the implicit AND that
        // FTS5 applies between phrases.
        var ftsLiteralQuery = string.Join(
            ' ',
            query
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(term => $"\"{term.Replace("\"", "\"\"")}\""));

        using var cmd = _connection.CreateCommand();
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

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM VaultIndex;";
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
