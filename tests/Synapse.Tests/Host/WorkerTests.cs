using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Synapse.Core.Ports;
using Synapse.Host;
using Synapse.Rules;
using Synapse.Sync.Auth;
using Synapse.Sync.Config;
using Synapse.Sync.GitHub;
using Synapse.Sync.Reconciliation;

namespace Synapse.Tests.Host;

public class WorkerTests : IDisposable
{
    private readonly string _tempVaultDir;
    private readonly string _tempDbDir;

    public WorkerTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-vault-test-{Guid.NewGuid():N}");
        _tempDbDir = Path.Combine(Path.GetTempPath(), $"synapse-db-test-{Guid.NewGuid():N}");
    }

    [Fact]
    public void Resolve_WhenAppsettingsVaultPathIsEmptyAndSavedConfigHasVaultPath_ShouldUseSavedConfig()
    {
        // Simula appsettings.json com "Synapse:VaultPath": ""
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Synapse:VaultPath", "" },
            { "Synapse:DatabasePath", "" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var savedConfig = new SynapseConfig
        {
            VaultPath = _tempVaultDir,
            DatabasePath = Path.Combine(_tempDbDir, "synapse.db")
        };

        var resolved = SynapseHostPaths.Resolve(savedConfig, configuration);

        resolved.VaultPath.ShouldBe(_tempVaultDir);
        resolved.DatabasePath.ShouldBe(Path.Combine(_tempDbDir, "synapse.db"));
    }

    [Fact]
    public void Resolve_WhenSavedConfigAndAppsettingsAreEmpty_ShouldFallbackToDefaultPath()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Synapse:VaultPath", "" },
            { "Synapse:DatabasePath", "" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var savedConfig = new SynapseConfig
        {
            VaultPath = string.Empty,
            DatabasePath = string.Empty
        };

        var resolved = SynapseHostPaths.Resolve(savedConfig, configuration);

        var expectedDefaultVault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SynapseVault");
        resolved.VaultPath.ShouldBe(expectedDefaultVault);
    }

    [Fact]
    public async Task Worker_WhenVaultPathResolvedFromSavedConfigWithEmptyAppsettings_ShouldRunSuccessfullyWithoutException()
    {
        // 1. Configuração com appsettings vazio e config salva preenchida
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Synapse:VaultPath", "" },
            { "Synapse:DatabasePath", "" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var savedConfig = new SynapseConfig
        {
            VaultPath = _tempVaultDir,
            DatabasePath = Path.Combine(_tempDbDir, "synapse.db")
        };

        var paths = SynapseHostPaths.Resolve(savedConfig, configuration);

        // 2. Mock das dependências
        var indexStore = Substitute.For<ISyncIndexStore>();
        var cloudProvider = Substitute.For<ICloudProvider>();
        var conflictResolver = Substitute.For<IConflictResolver>();
        var fileSystem = Substitute.For<IFileSystem>();
        var ruleEngine = Substitute.For<IRuleEngine>();
        var tokenStore = Substitute.For<ITokenStore>();
        var gitHubConfig = new GitHubClientConfig();
        var authManager = new GitHubAuthManager(tokenStore, gitHubConfig);
        var vaultWatcher = Substitute.For<IVaultWatcher>();
        var logger = Substitute.For<ILogger<Worker>>();
        var loggerFactory = NullLoggerFactory.Instance;

        using var worker = new Worker(
            indexStore,
            cloudProvider,
            conflictResolver,
            fileSystem,
            ruleEngine,
            authManager,
            gitHubConfig,
            vaultWatcher,
            paths,
            logger,
            loggerFactory);

        using var cts = new CancellationTokenSource();

        // 3. Executa o worker e cancela logo após inicializar
        var startTask = worker.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // 4. Valida que o cofre foi criado e o watcher iniciado no caminho resolvido
        Directory.Exists(_tempVaultDir).ShouldBeTrue();
        vaultWatcher.Received().Start(_tempVaultDir);
    }

    [Fact]
    public async Task Worker_WhenVaultPathIsEmpty_ShouldLogWarningAndStopGracefullyWithoutThrowingArgumentException()
    {
        var paths = new SynapseHostPaths(string.Empty, string.Empty);

        var indexStore = Substitute.For<ISyncIndexStore>();
        var cloudProvider = Substitute.For<ICloudProvider>();
        var conflictResolver = Substitute.For<IConflictResolver>();
        var fileSystem = Substitute.For<IFileSystem>();
        var ruleEngine = Substitute.For<IRuleEngine>();
        var tokenStore = Substitute.For<ITokenStore>();
        var gitHubConfig = new GitHubClientConfig();
        var authManager = new GitHubAuthManager(tokenStore, gitHubConfig);
        var vaultWatcher = Substitute.For<IVaultWatcher>();
        var logger = Substitute.For<ILogger<Worker>>();
        var loggerFactory = NullLoggerFactory.Instance;

        using var worker = new Worker(
            indexStore,
            cloudProvider,
            conflictResolver,
            fileSystem,
            ruleEngine,
            authManager,
            gitHubConfig,
            vaultWatcher,
            paths,
            logger,
            loggerFactory);

        using var cts = new CancellationTokenSource();

        // Deve executar e retornar sem lançar exceção
        await Should.NotThrowAsync(async () =>
        {
            await worker.StartAsync(cts.Token);
            await Task.Delay(50);
            await worker.StopAsync(CancellationToken.None);
        });

        vaultWatcher.DidNotReceive().Start(Arg.Any<string>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
        if (Directory.Exists(_tempDbDir))
        {
            try { Directory.Delete(_tempDbDir, true); } catch { }
        }
    }
}
