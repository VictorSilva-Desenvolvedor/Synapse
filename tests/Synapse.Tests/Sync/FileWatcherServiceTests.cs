using Shouldly;
using Synapse.Core.Ports;
using Synapse.Sync;

namespace Synapse.Tests.Sync;

// Nivel de integracao: FileSystemWatcher real sobre um diretorio temporario real (nao ha como testar
// isoladamente sem tocar o disco - e por isso que IVaultWatcher existe como porta, para que o resto do
// sistema use FakeVaultWatcher nos proprios testes, conforme Plano de Testes secao 6.1).
public class FileWatcherServiceTests : IDisposable
{
    private readonly string _vaultRoot;
    private readonly FileWatcherService _watcher;

    public FileWatcherServiceTests()
    {
        _vaultRoot = Path.Combine(Path.GetTempPath(), $"synapse-vault-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_vaultRoot);
        _watcher = new FileWatcherService();
    }

    public void Dispose()
    {
        _watcher.Dispose();
        if (Directory.Exists(_vaultRoot)) Directory.Delete(_vaultRoot, recursive: true);
    }

    private static async Task<VaultChangeEvent?> AguardarEvento(FileWatcherService watcher, Action disparar)
    {
        var tcs = new TaskCompletionSource<VaultChangeEvent>();
        EventHandler<VaultChangeEvent> handler = (_, e) => tcs.TrySetResult(e);
        watcher.Changed += handler;
        try
        {
            disparar();
            var completado = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            return completado == tcs.Task ? await tcs.Task : null;
        }
        finally
        {
            watcher.Changed -= handler;
        }
    }

    [Fact]
    public async Task ArquivoMdCriado_DisparaEventoCreatedComCaminhoRelativo()
    {
        _watcher.Start(_vaultRoot);
        var caminhoCompleto = Path.Combine(_vaultRoot, "nota.md");

        var evento = await AguardarEvento(_watcher, () => File.WriteAllText(caminhoCompleto, "conteudo"));

        evento.ShouldNotBeNull();
        evento.RelativePath.ShouldBe("nota.md");
        evento.EventType.ShouldBe(SyncEventType.Created);
    }

    [Fact]
    public async Task ArquivoComExtensaoNaoObservada_NaoDisparaEvento()
    {
        _watcher.Start(_vaultRoot);
        var caminhoCompleto = Path.Combine(_vaultRoot, "ignorado.tmp");

        var evento = await AguardarEvento(_watcher, () => File.WriteAllText(caminhoCompleto, "conteudo"));

        evento.ShouldBeNull();
    }

    [Fact]
    public async Task ExtensaoDeAnexoConfigurada_DisparaEvento()
    {
        using var watcherComAnexos = new FileWatcherService(attachmentExtensions: ["png"]);
        watcherComAnexos.Start(_vaultRoot);
        var caminhoCompleto = Path.Combine(_vaultRoot, "imagem.png");

        var evento = await AguardarEvento(watcherComAnexos, () => File.WriteAllBytes(caminhoCompleto, [1, 2, 3]));

        evento.ShouldNotBeNull();
        evento.RelativePath.ShouldBe("imagem.png");
    }

    [Fact]
    public async Task ArquivoEmSubpasta_UsaCaminhoRelativoComBarraNormal()
    {
        var subpasta = Path.Combine(_vaultRoot, "subpasta");
        Directory.CreateDirectory(subpasta);
        _watcher.Start(_vaultRoot);
        var caminhoCompleto = Path.Combine(subpasta, "nota.md");

        var evento = await AguardarEvento(_watcher, () => File.WriteAllText(caminhoCompleto, "conteudo"));

        evento.ShouldNotBeNull();
        evento.RelativePath.ShouldBe("subpasta/nota.md");
    }

    [Fact]
    public void Start_ChamadoDuasVezes_LancaExcecao()
    {
        _watcher.Start(_vaultRoot);

        Should.Throw<InvalidOperationException>(() => _watcher.Start(_vaultRoot));
    }

    [Fact]
    public async Task ArquivoDeletado_DisparaEventoDeleted()
    {
        var caminhoCompleto = Path.Combine(_vaultRoot, "nota.md");
        File.WriteAllText(caminhoCompleto, "conteudo");
        _watcher.Start(_vaultRoot);

        var evento = await AguardarEvento(_watcher, () => File.Delete(caminhoCompleto));

        evento.ShouldNotBeNull();
        evento.EventType.ShouldBe(SyncEventType.Deleted);
    }

    [Fact]
    public void Raise_WhenSubscriberThrows_DoesNotLetExceptionEscapeTheWatcherCallback()
    {
        // Raise roda no callback do FileSystemWatcher: qualquer excecao que escape dali derruba o
        // PROCESSO, nao so o evento. Foi assim que uma ArgumentNullException vinda de
        // Path.GetRelativePath(null, ...) matou o host de teste inteiro.
        using var watcher = new FileWatcherService();
        watcher.Start(_vaultRoot);
        watcher.Changed += (_, _) => throw new InvalidOperationException("assinante quebrado");

        Should.NotThrow(() => watcher.Raise(Path.Combine(_vaultRoot, "nota.md"), SyncEventType.Created));
    }

    [Fact]
    public async Task Raise_WhileStopRunsConcurrently_NeverThrows()
    {
        // Corrida real: Stop() zera o caminho raiz enquanto eventos de arquivo ainda chegam. A versao
        // anterior verificava o campo e depois o lia de novo, entao o Stop() cabia no meio e
        // Path.GetRelativePath recebia null.
        for (int rodada = 0; rodada < 50; rodada++)
        {
            var watcher = new FileWatcherService();
            watcher.Start(_vaultRoot);

            var disparos = Task.Run(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    watcher.Raise(Path.Combine(_vaultRoot, $"nota_{i}.md"), SyncEventType.Modified);
                }
            });

            var parada = Task.Run(() => watcher.Stop());

            await Should.NotThrowAsync(async () => await Task.WhenAll(disparos, parada));
            watcher.Dispose();
        }
    }
}
