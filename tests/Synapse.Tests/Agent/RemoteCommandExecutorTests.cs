using Shouldly;
using Synapse.Agent;
using Synapse.Agent.Models;
using Synapse.Sync.Config;

namespace Synapse.Tests.Agent;

public class RemoteCommandExecutorTests : IDisposable
{
    private readonly string _tempVaultDir;

    public RemoteCommandExecutorTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-agent-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRemoteControlDisabled_ShouldRejectAllCommands()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = false,
            VaultPath = _tempVaultDir
        };

        var executor = new RemoteCommandExecutor(config);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.OpenNote,
            Payload: new Dictionary<string, string> { ["relativePath"] = "Notas/Nota1.md" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("desativado");
    }

    [Fact]
    public async Task ExecuteAsync_OpenApp_WhenAppNotAllowed_ShouldReject()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            RemoteAllowedApps = new Dictionary<string, string>
            {
                ["calc"] = "calc.exe"
            }
        };

        var executor = new RemoteCommandExecutor(config);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.OpenApp,
            Payload: new Dictionary<string, string> { ["app"] = "malicious_app" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("não está na lista de aplicativos permitidos");
    }

    [Fact]
    public async Task ExecuteAsync_OpenApp_WhenAppMissingPayload_ShouldReject()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir
        };

        var executor = new RemoteCommandExecutor(config);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.OpenApp,
            Payload: new Dictionary<string, string>(),
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("Parâmetro 'app' não informado");
    }

    [Fact]
    public async Task ExecuteAsync_OpenNote_WhenPathTraversalAttempted_ShouldReject()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir
        };

        var executor = new RemoteCommandExecutor(config);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.OpenNote,
            Payload: new Dictionary<string, string> { ["relativePath"] = "../../Windows/System32/cmd.exe" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("Path Traversal bloqueado");
    }

    [Fact]
    public async Task ExecuteAsync_OpenNote_WhenNoteNotFound_ShouldReturnFailed()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir
        };

        var executor = new RemoteCommandExecutor(config);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.OpenNote,
            Payload: new Dictionary<string, string> { ["relativePath"] = "Inexistente.md" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Failed);
        result.Message.ShouldContain("não foi encontrada");
    }

    [Fact]
    public async Task ExecuteAsync_OpenNote_WhenNoteExists_ShouldSucceed()
    {
        var notePath = Path.Combine(_tempVaultDir, "Notas", "MinhaNota.md");
        Directory.CreateDirectory(Path.GetDirectoryName(notePath)!);
        await File.WriteAllTextAsync(notePath, "# Minha Nota");

        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir
        };

        var executor = new RemoteCommandExecutor(config);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.OpenNote,
            Payload: new Dictionary<string, string> { ["relativePath"] = "Notas/MinhaNota.md" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Success);
        result.Message.ShouldContain("aberta com sucesso");
    }

    [Fact]
    public async Task ExecuteAsync_FocusWindow_WhenProcessNotFound_ShouldHandleGracefully()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir
        };

        var executor = new RemoteCommandExecutor(config);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.FocusWindow,
            Payload: new Dictionary<string, string> { ["processName"] = "non_existent_process_12345" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Success);
        result.Message.ShouldContain("não está em execução");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }
}
