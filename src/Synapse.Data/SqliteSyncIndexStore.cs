using Microsoft.Data.Sqlite;
using Synapse.Core.Ports;

namespace Synapse.Data;

/// <summary>
/// Implementação SQLite de ISyncIndexStore (RF-SYNC.3/4, ADR-002), sobre o esquema de 4 tabelas de
/// SRS - Synapse.md seção 3.3 (SyncedFiles, SyncQueue, Conflicts, SyncState). Mantém uma única conexão
/// aberta durante toda a vida do objeto - coerente com o modelo de concorrência do SAD (seção 4): um
/// único SyncQueueProcessor é quem escreve, SQLite é single-writer por natureza.
/// </summary>
public sealed class SqliteSyncIndexStore : ISyncIndexStore, IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteSyncIndexStore(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        EnsureSchema();
    }

    // Pooling=false: esta conexao vive pelo tempo de vida do objeto (nao e um cenario de muitas conexoes
    // curtas), e sem isso o arquivo fica com handle preso pelo pool mesmo depois do Dispose().
    public static SqliteSyncIndexStore ForFile(string databaseFilePath) =>
        new(new SqliteConnectionStringBuilder { DataSource = databaseFilePath, Pooling = false }.ToString());

    private void EnsureSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SyncedFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LocalPath TEXT NOT NULL UNIQUE,
                CloudFileId TEXT NULL,
                ContentHash TEXT NOT NULL,
                LocalMtime INTEGER NOT NULL,
                CloudModifiedTime INTEGER NULL,
                LastSyncedAt INTEGER NOT NULL,
                Status TEXT NOT NULL,
                CloudContentHash TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SyncedFiles_CloudFileId ON SyncedFiles (CloudFileId);

            CREATE TABLE IF NOT EXISTS SyncQueue (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FilePath TEXT NOT NULL,
                EventType TEXT NOT NULL,
                EnqueuedAt INTEGER NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0,
                LastError TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS Conflicts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FilePath TEXT NOT NULL,
                LocalVersionPath TEXT NOT NULL,
                RemoteVersionPath TEXT NOT NULL,
                DetectedAt INTEGER NOT NULL,
                ResolutionStatus TEXT NOT NULL DEFAULT 'Unresolved'
            );

            CREATE TABLE IF NOT EXISTS SyncState (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                DriveChangesPageToken TEXT NULL,
                LastFullSyncAt INTEGER NULL
            );
            """;
        command.ExecuteNonQuery();

        MigrateSchema();
    }

    private void MigrateSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(SyncedFiles);";
        using var reader = command.ExecuteReader();
        var hasCloudContentHash = false;
        while (reader.Read())
        {
            var name = reader.GetString(1);
            if (string.Equals(name, "CloudContentHash", StringComparison.OrdinalIgnoreCase))
            {
                hasCloudContentHash = true;
                break;
            }
        }
        reader.Close();

        if (!hasCloudContentHash)
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE SyncedFiles ADD COLUMN CloudContentHash TEXT NULL;";
            alterCmd.ExecuteNonQuery();
        }
    }

    public async Task<SyncedFileRecord?> FindByLocalPathAsync(string localPath, CancellationToken ct)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Id, LocalPath, CloudFileId, ContentHash, LocalMtime, CloudModifiedTime, LastSyncedAt, Status, CloudContentHash FROM SyncedFiles WHERE LocalPath = $localPath";
        command.Parameters.AddWithValue("$localPath", localPath);

        using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadSyncedFile(reader) : null;
    }

    public async Task<SyncedFileRecord?> FindByCloudFileIdAsync(string cloudFileId, CancellationToken ct)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Id, LocalPath, CloudFileId, ContentHash, LocalMtime, CloudModifiedTime, LastSyncedAt, Status, CloudContentHash FROM SyncedFiles WHERE CloudFileId = $cloudFileId";
        command.Parameters.AddWithValue("$cloudFileId", cloudFileId);

        using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadSyncedFile(reader) : null;
    }

    // Upsert por LocalPath (chave natural do arquivo): Id e sempre gerido pelo SQLite via AUTOINCREMENT,
    // nunca pelo valor que o chamador passa em SyncedFileRecord.Id (irrelevante em uma insercao nova).
    public async Task UpsertAsync(SyncedFileRecord record, CancellationToken ct)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SyncedFiles (LocalPath, CloudFileId, ContentHash, LocalMtime, CloudModifiedTime, LastSyncedAt, Status, CloudContentHash)
            VALUES ($localPath, $cloudFileId, $contentHash, $localMtime, $cloudModifiedTime, $lastSyncedAt, $status, $cloudContentHash)
            ON CONFLICT (LocalPath) DO UPDATE SET
                CloudFileId = excluded.CloudFileId,
                ContentHash = excluded.ContentHash,
                LocalMtime = excluded.LocalMtime,
                CloudModifiedTime = excluded.CloudModifiedTime,
                LastSyncedAt = excluded.LastSyncedAt,
                Status = excluded.Status,
                CloudContentHash = excluded.CloudContentHash
            """;
        command.Parameters.AddWithValue("$localPath", record.LocalPath);
        command.Parameters.AddWithValue("$cloudFileId", (object?)record.CloudFileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$contentHash", record.ContentHash);
        command.Parameters.AddWithValue("$localMtime", record.LocalMtime.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$cloudModifiedTime", record.CloudModifiedTime.HasValue ? record.CloudModifiedTime.Value.ToUnixTimeMilliseconds() : DBNull.Value);
        command.Parameters.AddWithValue("$lastSyncedAt", record.LastSyncedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$status", record.Status.ToString());
        command.Parameters.AddWithValue("$cloudContentHash", (object?)record.CloudContentHash ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveAsync(string localPath, CancellationToken ct)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM SyncedFiles WHERE LocalPath = $localPath";
        command.Parameters.AddWithValue("$localPath", localPath);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task EnqueueAsync(SyncQueueItem item, CancellationToken ct)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SyncQueue (FilePath, EventType, EnqueuedAt, Attempts, LastError)
            VALUES ($filePath, $eventType, $enqueuedAt, $attempts, $lastError)
            """;
        command.Parameters.AddWithValue("$filePath", item.FilePath);
        command.Parameters.AddWithValue("$eventType", item.EventType.ToString());
        command.Parameters.AddWithValue("$enqueuedAt", item.EnqueuedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$attempts", item.Attempts);
        command.Parameters.AddWithValue("$lastError", (object?)item.LastError ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct);
    }

    // Contrato (API - Synapse.md secao 2.2): nao remove o item da fila - so MarkDoneAsync/MarkFailedAsync
    // fazem isso. Sustenta RF-SYNC.4: uma queda entre o Peek e o MarkDone deixa o item pendente de novo.
    public async Task<SyncQueueItem?> PeekNextAsync(CancellationToken ct)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Id, FilePath, EventType, EnqueuedAt, Attempts, LastError FROM SyncQueue ORDER BY Id ASC LIMIT 1";

        using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadQueueItem(reader) : null;
    }

    public async Task MarkDoneAsync(long queueItemId, CancellationToken ct)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM SyncQueue WHERE Id = $id";
        command.Parameters.AddWithValue("$id", queueItemId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(long queueItemId, string error, CancellationToken ct)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE SyncQueue SET Attempts = Attempts + 1, LastError = $error WHERE Id = $id";
        command.Parameters.AddWithValue("$id", queueItemId);
        command.Parameters.AddWithValue("$error", error);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetChangesPageTokenAsync(CancellationToken ct)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT DriveChangesPageToken FROM SyncState WHERE Id = 1";

        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (string)result;
    }

    public async Task SaveChangesPageTokenAsync(string pageToken, CancellationToken ct)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SyncState (Id, DriveChangesPageToken)
            VALUES (1, $pageToken)
            ON CONFLICT (Id) DO UPDATE SET DriveChangesPageToken = excluded.DriveChangesPageToken
            """;
        command.Parameters.AddWithValue("$pageToken", pageToken);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordConflictAsync(ConflictRecord record, CancellationToken ct)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Conflicts (FilePath, LocalVersionPath, RemoteVersionPath, DetectedAt)
            VALUES ($filePath, $localVersionPath, $remoteVersionPath, $detectedAt)
            """;
        command.Parameters.AddWithValue("$filePath", record.FilePath);
        command.Parameters.AddWithValue("$localVersionPath", record.LocalVersionPath);
        command.Parameters.AddWithValue("$remoteVersionPath", record.RemoteVersionPath);
        command.Parameters.AddWithValue("$detectedAt", record.DetectedAt.ToUnixTimeMilliseconds());

        await command.ExecuteNonQueryAsync(ct);
    }

    private static SyncedFileRecord ReadSyncedFile(SqliteDataReader reader) => new(
        Id: reader.GetInt64(0),
        LocalPath: reader.GetString(1),
        CloudFileId: reader.IsDBNull(2) ? null : reader.GetString(2),
        ContentHash: reader.GetString(3),
        LocalMtime: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)),
        CloudModifiedTime: reader.IsDBNull(5) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)),
        LastSyncedAt: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)),
        Status: Enum.Parse<SyncStatus>(reader.GetString(7)),
        CloudContentHash: reader.IsDBNull(8) ? null : reader.GetString(8));

    private static SyncQueueItem ReadQueueItem(SqliteDataReader reader) => new(
        Id: reader.GetInt64(0),
        FilePath: reader.GetString(1),
        EventType: Enum.Parse<SyncEventType>(reader.GetString(2)),
        EnqueuedAt: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
        Attempts: reader.GetInt32(4),
        LastError: reader.IsDBNull(5) ? null : reader.GetString(5));

    public void Dispose() => _connection.Dispose();
}
