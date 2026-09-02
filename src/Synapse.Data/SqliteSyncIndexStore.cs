using Microsoft.Data.Sqlite;
using Synapse.Core.Ports;

namespace Synapse.Data;

/// <summary>
/// Implementação SQLite de ISyncIndexStore (RF-SYNC.3/4, ADR-002), sobre o esquema de 4 tabelas de
/// SRS - Synapse.md seção 3.3 (SyncedFiles, SyncQueue, Conflicts, SyncState).
///
/// Cada operação abre a própria conexão. A versão anterior mantinha uma única conexão aberta durante
/// toda a vida do objeto, justificada por "um único SyncQueueProcessor é quem escreve" - e essa premissa
/// é falsa no Host: o Worker sobe quatro Task.Run em paralelo, e três delas tocam este mesmo singleton
/// (a fila de eventos, o poller remoto e a reconciliação). SqliteConnection guarda a lista de comandos
/// vivos numa List sem sincronização, então o Dispose concorrente de dois comandos em threads diferentes
/// derruba o processo com ArgumentOutOfRangeException dentro de RemoveCommand - reproduzido em teste,
/// 3 de 3 execuções.
///
/// Nenhum método depende de estado de conexão (não há transação, nem last_insert_rowid), então separar
/// as conexões é seguro. journal_mode=WAL mantém leitura e escrita simultâneas, e busy_timeout faz o
/// segundo escritor esperar a vez em vez de falhar na hora com SQLITE_BUSY.
/// </summary>
public sealed class SqliteSyncIndexStore : ISyncIndexStore, IDisposable
{
    private readonly string _connectionString;

    public SqliteSyncIndexStore(string connectionString)
    {
        _connectionString = connectionString;
        EnsureSchema();
    }

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

    // Pooling=false: agora SAO muitas conexoes curtas, uma por operacao, e a tentacao e ligar o pool.
    // Continua desligado porque o pool segura o handle do arquivo depois do Close() - com ele, apagar ou
    // reconstruir o banco falha, e os testes nao conseguem limpar o arquivo temporario. O custo de abrir
    // uma conexao SQLite local e pequeno perto de uma corrupcao de estado compartilhado.
    //
    // O diretorio e criado aqui porque o SQLite nao cria pasta: ele cria o ARQUIVO do banco, e falha com
    // SqliteException("unable to open database file") se a pasta nao existir. O caminho padrao e
    // %LOCALAPPDATA%\Synapse\synapse.db, e ate agora isso so funcionava porque outra coisa (o log, a
    // configuracao) criava essa pasta antes por acaso - ninguem garantia a ordem. Bastava o Host subir
    // primeiro numa maquina limpa para quebrar no arranque.
    public static SqliteSyncIndexStore ForFile(string databaseFilePath)
    {
        var directory = Path.GetDirectoryName(databaseFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new(new SqliteConnectionStringBuilder { DataSource = databaseFilePath, Pooling = false }.ToString());
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
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
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
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
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE SyncedFiles ADD COLUMN CloudContentHash TEXT NULL;";
            alterCmd.ExecuteNonQuery();
        }
    }

    public async Task<SyncedFileRecord?> FindByLocalPathAsync(string localPath, CancellationToken ct)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, LocalPath, CloudFileId, ContentHash, LocalMtime, CloudModifiedTime, LastSyncedAt, Status, CloudContentHash FROM SyncedFiles WHERE LocalPath = $localPath";
        command.Parameters.AddWithValue("$localPath", localPath);

        using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadSyncedFile(reader) : null;
    }

    public async Task<SyncedFileRecord?> FindByCloudFileIdAsync(string cloudFileId, CancellationToken ct)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, LocalPath, CloudFileId, ContentHash, LocalMtime, CloudModifiedTime, LastSyncedAt, Status, CloudContentHash FROM SyncedFiles WHERE CloudFileId = $cloudFileId";
        command.Parameters.AddWithValue("$cloudFileId", cloudFileId);

        using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadSyncedFile(reader) : null;
    }

    // Upsert por LocalPath (chave natural do arquivo): Id e sempre gerido pelo SQLite via AUTOINCREMENT,
    // nunca pelo valor que o chamador passa em SyncedFileRecord.Id (irrelevante em uma insercao nova).
    public async Task UpsertAsync(SyncedFileRecord record, CancellationToken ct)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
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
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SyncedFiles WHERE LocalPath = $localPath";
        command.Parameters.AddWithValue("$localPath", localPath);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task EnqueueAsync(SyncQueueItem item, CancellationToken ct)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
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
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, FilePath, EventType, EnqueuedAt, Attempts, LastError FROM SyncQueue ORDER BY Id ASC LIMIT 1";

        using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadQueueItem(reader) : null;
    }

    public async Task MarkDoneAsync(long queueItemId, CancellationToken ct)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SyncQueue WHERE Id = $id";
        command.Parameters.AddWithValue("$id", queueItemId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(long queueItemId, string error, CancellationToken ct)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE SyncQueue SET Attempts = Attempts + 1, LastError = $error WHERE Id = $id";
        command.Parameters.AddWithValue("$id", queueItemId);
        command.Parameters.AddWithValue("$error", error);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetChangesPageTokenAsync(CancellationToken ct)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DriveChangesPageToken FROM SyncState WHERE Id = 1";

        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (string)result;
    }

    public async Task SaveChangesPageTokenAsync(string pageToken, CancellationToken ct)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
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
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
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

    // Nada a liberar: nenhuma conexao sobrevive a operacao que a abriu. Mantido porque ISyncIndexStore
    // e registrado como singleton no DI e o container chama Dispose no encerramento.
    public void Dispose()
    {
    }
}
