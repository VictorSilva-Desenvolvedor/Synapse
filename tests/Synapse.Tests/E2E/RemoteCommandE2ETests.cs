using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;
using Synapse.Agent;
using Synapse.Agent.Models;
using Synapse.Sync.Auth;
using Synapse.Sync.Config;
using Synapse.Sync.GitHub;

namespace Synapse.Tests.E2E;

/// <summary>
/// Validação E2E Real do subsistema Synapse Remote contra o repositório GitHub real (synapse-e2e-test-vault).
/// Simula o celular enviando um comando via GitHub Relay e o PC executando e respondendo.
/// </summary>
[Collection("RealE2E")]
public class RemoteCommandE2ETests : IDisposable
{
    private readonly string _tempVaultDir;
    private readonly string _tempCursorFile;
    private readonly string _tempTokenFile;
    private readonly string? _gitHubToken;
    private const string TestOwner = "VictorSilva-Desenvolvedor";
    private const string TestRepo = "synapse-e2e-test-vault";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    public RemoteCommandE2ETests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-remote-e2e-vault-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);

        _tempCursorFile = Path.Combine(Path.GetTempPath(), $"synapse-remote-cursor-{Guid.NewGuid():N}.txt");
        _tempTokenFile = Path.Combine(Path.GetTempPath(), $"synapse-remote-token-{Guid.NewGuid():N}.dat");

        _gitHubToken = Environment.GetEnvironmentVariable("SYNAPSE_REMOTE_TOKEN");
        if (string.IsNullOrWhiteSpace(_gitHubToken))
        {
            _gitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        }

        if (string.IsNullOrWhiteSpace(_gitHubToken))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "gh",
                    Arguments = "auth token",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    _gitHubToken = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();
                }
            }
            catch { }
        }
    }

    [Fact]
    public async Task E2E_Scenario_RemoteCommand_OpenNote_RealGitHubRelayExecution()
    {
        if (string.IsNullOrWhiteSpace(_gitHubToken)) return;

        // 1. Configura autenticação dedicada com DPAPI
        var tokenStore = new DpapiTokenStore(_tempTokenFile);
        await tokenStore.SaveTokenAsync(new GitHubToken(_gitHubToken, TestOwner));

        var clientConfig = new GitHubClientConfig
        {
            Owner = TestOwner,
            Repository = TestRepo,
            Branch = "main"
        };

        var authManager = new GitHubAuthManager(tokenStore, clientConfig);
        var gitHubProvider = new GitHubProvider(authManager, clientConfig);

        // 2. Prepara nota real no cofre local
        var noteRelPath = "Notas/NotaRemotaE2E.md";
        var noteFullPath = Path.Combine(_tempVaultDir, noteRelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(noteFullPath)!);
        await File.WriteAllTextAsync(noteFullPath, $"# Nota para Teste Remoto\nCriada em {DateTime.UtcNow:O}");

        // 3. Captura o cursor base do repositório antes do comando chegar
        var baselineToken = await gitHubProvider.GetStartPageTokenAsync(CancellationToken.None);
        await File.WriteAllTextAsync(_tempCursorFile, baselineToken, Encoding.UTF8);

        // 4. Simula o "celular" escrevendo um comando direto no repositório GitHub via UploadAsync
        var commandId = Guid.NewGuid();
        var remoteCommand = new RemoteCommand(
            Id: commandId,
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.OpenNote,
            Payload: new Dictionary<string, string> { ["relativePath"] = noteRelPath },
            RequestedBy: "mobile-device-e2e");

        var localCmdTemp = Path.Combine(Path.GetTempPath(), $"{commandId}.json");
        await File.WriteAllTextAsync(localCmdTemp, JsonSerializer.Serialize(remoteCommand, JsonOptions), Encoding.UTF8);

        await gitHubProvider.UploadAsync(localCmdTemp, ".synapse/remote/commands", CancellationToken.None);
        if (File.Exists(localCmdTemp)) File.Delete(localCmdTemp);

        // 4. Configura o agente e executa um ciclo de polling real
        var synapseConfig = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            Owner = TestOwner,
            Repository = TestRepo,
            Branch = "main"
        };

        var executor = new RemoteCommandExecutor(synapseConfig);
        var auditLog = new RemoteAuditLog(_tempVaultDir);

        var poller = new RemoteCommandPoller(
            cloudProvider: gitHubProvider,
            executor: executor,
            auditLog: auditLog,
            cursorFilePath: _tempCursorFile);

        // Executa o ciclo de polling que baixa o comando, executa e sobe o resultado
        await poller.RunOnceAsync(CancellationToken.None);

        // 5. Verifica se o resultado foi publicado no GitHub
        var localResultTemp = Path.Combine(Path.GetTempPath(), $"downloaded-res-{commandId}.json");
        await gitHubProvider.DownloadAsync($".synapse/remote/results/{commandId}.json", localResultTemp, CancellationToken.None);

        File.Exists(localResultTemp).ShouldBeTrue();
        var resultJson = await File.ReadAllTextAsync(localResultTemp);
        if (File.Exists(localResultTemp)) File.Delete(localResultTemp);

        var result = JsonSerializer.Deserialize<RemoteCommandResult>(resultJson, JsonOptions);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(commandId);
        result.Status.ShouldBe(RemoteCommandStatus.Success);
        result.Message.ShouldContain("aberta com sucesso");

        // 6. Confirma que a trilha de auditoria local foi gravada
        var auditFile = Path.Combine(_tempVaultDir, ".synapse", "remote-audit.log");
        File.Exists(auditFile).ShouldBeTrue();
        var auditContent = await File.ReadAllTextAsync(auditFile);
        auditContent.ShouldContain(commandId.ToString());
        auditContent.ShouldContain("mobile-device-e2e");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
        if (File.Exists(_tempCursorFile))
        {
            try { File.Delete(_tempCursorFile); } catch { }
        }
        if (File.Exists(_tempTokenFile))
        {
            try { File.Delete(_tempTokenFile); } catch { }
        }
    }
}
