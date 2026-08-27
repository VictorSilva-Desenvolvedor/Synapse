using System.Text.Json;
using System.Text.Json.Serialization;
using NSubstitute;
using Shouldly;
using Synapse.Agent;
using Synapse.Agent.Models;
using Synapse.Core.Ports;
using Synapse.Sync.Config;

namespace Synapse.Tests.Agent;

public class RemoteCommandPollerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cursorFile;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public RemoteCommandPollerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"synapse-poller-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _cursorFile = Path.Combine(_tempDir, "cursor.txt");
    }

    [Fact]
    public async Task RunOnceAsync_WhenNewCommandArrives_ShouldDownloadExecuteUploadResultAndSaveCursor()
    {
        var commandId = Guid.NewGuid();
        var command = new RemoteCommand(
            Id: commandId,
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.OpenNote,
            Payload: new Dictionary<string, string> { ["relativePath"] = "Notas/Test.md" },
            RequestedBy: "mobile-user");

        var notePath = Path.Combine(_tempDir, "Notas", "Test.md");
        Directory.CreateDirectory(Path.GetDirectoryName(notePath)!);
        await File.WriteAllTextAsync(notePath, "# Test Content");

        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempDir
        };

        var executor = new RemoteCommandExecutor(config);
        var auditLog = new RemoteAuditLog(_tempDir);
        var mockCloud = Substitute.For<ICloudProvider>();

        var cloudFile = new CloudFile(
            Id: $".synapse/remote/commands/{commandId}.json",
            Name: $"{commandId}.json",
            Md5Checksum: "sha123",
            ModifiedTime: DateTimeOffset.UtcNow,
            Trashed: false);

        mockCloud.GetStartPageTokenAsync(Arg.Any<CancellationToken>())
            .Returns("token-start");

        mockCloud.GetChangesAsync("token-start", Arg.Any<CancellationToken>())
            .Returns(new ChangesPage(
                ChangedFiles: [cloudFile],
                NextPageToken: null,
                NewStartPageToken: "token-next"));

        mockCloud.When(c => c.DownloadAsync(cloudFile.Id, Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(callInfo =>
            {
                var destPath = callInfo.ArgAt<string>(1);
                File.WriteAllText(destPath, JsonSerializer.Serialize(command, JsonOptions));
            });

        var poller = new RemoteCommandPoller(
            mockCloud,
            executor,
            auditLog,
            cursorFilePath: _cursorFile);

        await poller.RunOnceAsync(CancellationToken.None);

        // Verifica se fez o download do comando
        await mockCloud.Received(1).DownloadAsync(cloudFile.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Verifica se fez o upload do resultado
        await mockCloud.Received(1).UploadAsync(
            Arg.Is<string>(p => p.EndsWith($"{commandId}.json")),
            ".synapse/remote/results",
            Arg.Any<CancellationToken>());

        // Verifica se salvou o cursor
        File.Exists(_cursorFile).ShouldBeTrue();
        (await File.ReadAllTextAsync(_cursorFile)).Trim().ShouldBe("token-next");

        // Verifica se gerou o log de auditoria
        var auditFile = Path.Combine(_tempDir, ".synapse", "remote-audit.log");
        File.Exists(auditFile).ShouldBeTrue();
        var auditContent = await File.ReadAllTextAsync(auditFile);
        auditContent.ShouldContain(commandId.ToString());
        auditContent.ShouldContain("Success");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
