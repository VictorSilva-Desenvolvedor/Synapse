using Shouldly;
using Synapse.Data;
using Xunit;

namespace Synapse.Tests.Data;

/// <summary>
/// O caminho padrao do banco e %LOCALAPPDATA%\Synapse\synapse.db. O SQLite cria o arquivo, mas nao a
/// pasta: sem ela, o Host morria no arranque com SqliteException. Ate agora isso passava despercebido
/// porque o log e a configuracao costumavam criar a pasta antes - por acaso, nao por garantia.
/// </summary>
public class SqliteSyncIndexStoreForFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"synapse-store-dir-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ForFile_CriaODiretorioQuandoEleNaoExiste()
    {
        var dbPath = Path.Combine(_root, "Synapse", "synapse.db");
        Directory.Exists(_root).ShouldBeFalse();

        using var store = SqliteSyncIndexStore.ForFile(dbPath);

        File.Exists(dbPath).ShouldBeTrue();
    }
}
