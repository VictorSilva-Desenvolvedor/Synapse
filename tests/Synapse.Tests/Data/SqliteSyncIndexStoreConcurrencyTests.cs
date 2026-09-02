using Shouldly;
using Synapse.Core.Ports;
using Synapse.Data;
using Xunit;

namespace Synapse.Tests.Data;

/// <summary>
/// O Worker do Host roda quatro Task.Run em paralelo, e tres delas tocam o MESMO
/// ISyncIndexStore singleton (fila de eventos, poller remoto, reconciliacao). O comentario
/// da classe afirma escritor unico. Este teste mede se a conexao compartilhada aguenta.
/// </summary>
public class SqliteSyncIndexStoreConcurrencyTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"synapse-conc-{Guid.NewGuid():N}.db");
    private readonly SqliteSyncIndexStore _store;

    public SqliteSyncIndexStoreConcurrencyTests() => _store = SqliteSyncIndexStore.ForFile(_dbPath);

    public void Dispose()
    {
        _store.Dispose();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(f); } catch { }
        }
    }

    private static SyncedFileRecord Registro(string path) => new(
        Id: 0, LocalPath: path, CloudFileId: "id", ContentHash: "h",
        LocalMtime: DateTimeOffset.UtcNow, CloudModifiedTime: DateTimeOffset.UtcNow,
        LastSyncedAt: DateTimeOffset.UtcNow, Status: SyncStatus.Synced);

    [Fact]
    public async Task TresLacosSimultaneos_ComoNoWorker()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = cts.Token;

        var escritor = Task.Run(async () =>
        {
            for (var i = 0; i < 300; i++)
            {
                await _store.UpsertAsync(Registro($"notas/a{i}.md"), ct);
                await _store.EnqueueAsync(new SyncQueueItem(0, $"notas/a{i}.md", SyncEventType.Modified, DateTimeOffset.UtcNow, 0, null), ct);
            }
        }, ct);

        var fila = Task.Run(async () =>
        {
            for (var i = 0; i < 300; i++)
            {
                await _store.PeekNextAsync(ct);
            }
        }, ct);

        var leitor = Task.Run(async () =>
        {
            for (var i = 0; i < 300; i++)
            {
                await _store.FindByLocalPathAsync($"notas/a{i}.md", ct);
            }
        }, ct);

        var erro = await Record.ExceptionAsync(() => Task.WhenAll(escritor, fila, leitor));

        erro.ShouldBeNull($"conexao compartilhada quebrou: {erro?.GetType().Name}: {erro?.Message}");
    }
}
