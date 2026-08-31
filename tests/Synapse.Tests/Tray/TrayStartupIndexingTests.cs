using NSubstitute;
using Shouldly;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;
using Synapse.Sync.Config;
using Synapse.Tests.UI;
using Synapse.Tray;
using Xunit;

namespace Synapse.Tests.Tray;

[Collection(WpfCaptureCollection.Name)]
public class TrayStartupIndexingTests : IDisposable
{
    private readonly WpfAppFixture _fixture;
    private readonly string _tempVaultDir;
    private readonly string _tempConfigDir;

    public TrayStartupIndexingTests(WpfAppFixture fixture)
    {
        _fixture = fixture;
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-tray-vault-{Guid.NewGuid():N}");
        _tempConfigDir = Path.Combine(Path.GetTempPath(), $"synapse-tray-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
        Directory.CreateDirectory(_tempConfigDir);
    }

    [Fact]
    public async Task CheckInitialOnboardingAsync_WhenVaultConfiguredWithoutRemoteAgentToken_ShouldTriggerBackgroundIndexing()
    {
        // Cria nota no cofre
        var notePath = Path.Combine(_tempVaultDir, "NotaInicial.md");
        await File.WriteAllTextAsync(notePath, "Conteúdo da nota inicial para testar indexação no startup.");

        // Configura cofre e repo sem token do agente remoto
        var configPath = Path.Combine(_tempConfigDir, "synapse_config.json");
        var configManager = new SynapseConfigManager(configPath);
        var config = new SynapseConfig
        {
            VaultPath = _tempVaultDir,
            Owner = "VictorSilva",
            Repository = "MeuCofre",
            Branch = "main",
            RemoteControlEnabled = false
        };
        await configManager.SaveAsync(config);

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.1f, 0.2f }));
        var mockAi = Substitute.For<IBrainAiProvider>();

        SynapseTrayApp? app = null;
        try
        {
            await _fixture.Invoke(async () =>
            {
                app = new SynapseTrayApp(
                    configManager: configManager,
                    ragEngineFactory: cfg => new VaultRagEngine(mockEmbedding, mockAi, cfg));
                await app.CheckInitialOnboardingAsync();
            });

            // Aguarda a indexação em background completar
            var store = new FileVaultIndexStore();
            var timeout = DateTime.UtcNow.AddSeconds(10);
            Dictionary<string, Synapse.Brain.Models.NoteEmbeddingEntry>? loaded = null;

            while (DateTime.UtcNow < timeout)
            {
                loaded = await store.LoadAsync(_tempVaultDir);
                if (loaded != null && loaded.ContainsKey("NotaInicial.md"))
                {
                    break;
                }
                await Task.Delay(100);
            }

            loaded.ShouldNotBeNull("O índice em disco deveria ter sido criado pela indexação proativa.");
            loaded.ContainsKey("NotaInicial.md").ShouldBeTrue("A nota 'NotaInicial.md' deveria ter sido indexada no startup.");
            loaded["NotaInicial.md"].Tokens.ShouldContain("notainicial");

            await mockEmbedding.Received().GenerateEmbeddingAsync(
                Arg.Is<string>(s => s.Contains("Conteúdo da nota inicial")),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            if (app != null)
            {
                _fixture.Invoke(() => app.Dispose());
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }

        if (Directory.Exists(_tempConfigDir))
        {
            try { Directory.Delete(_tempConfigDir, true); } catch { }
        }

        // Limpa o índice gerado
        try
        {
            var store = new FileVaultIndexStore();
            var indexPath = store.GetIndexFilePath(_tempVaultDir);
            if (File.Exists(indexPath))
            {
                File.Delete(indexPath);
            }
        }
        catch { }
    }
}
