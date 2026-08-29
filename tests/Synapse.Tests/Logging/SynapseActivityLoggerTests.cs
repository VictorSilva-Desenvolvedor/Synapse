using Shouldly;
using Synapse.Core.Logging;
using Xunit;

namespace Synapse.Tests.Logging;

public sealed class SynapseActivityLoggerTests : IDisposable
{
    private readonly string _tempLocalLogsDir;
    private readonly string _tempVaultDir;

    public SynapseActivityLoggerTests()
    {
        _tempLocalLogsDir = Path.Combine(Path.GetTempPath(), "synapse-log-test-local-" + Guid.NewGuid().ToString("N"));
        _tempVaultDir = Path.Combine(Path.GetTempPath(), "synapse-log-test-vault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempLocalLogsDir);
        Directory.CreateDirectory(_tempVaultDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempLocalLogsDir)) Directory.Delete(_tempLocalLogsDir, true);
            if (Directory.Exists(_tempVaultDir)) Directory.Delete(_tempVaultDir, true);
        }
        catch { }
    }

    [Fact]
    public async Task LogClickAsync_ShouldWriteToBothLocalLogsAndVaultMarkdown()
    {
        var logger = new SynapseActivityLogger(_tempLocalLogsDir, _tempVaultDir);

        await logger.LogClickAsync("ChatVault", "BtnSend", "Pergunta: 'quem são meus amigos?'");

        // 1. Verifica destino local
        var jsonlFile = Path.Combine(_tempLocalLogsDir, "synapse_activity.jsonl");
        var logFile = Path.Combine(_tempLocalLogsDir, "synapse_activity.log");
        File.Exists(jsonlFile).ShouldBeTrue();
        File.Exists(logFile).ShouldBeTrue();

        var jsonlContent = await File.ReadAllTextAsync(jsonlFile);
        jsonlContent.ShouldContain("\"component\":\"ChatVault\"");
        jsonlContent.ShouldContain("\"action\":\"UserClick\"");
        jsonlContent.ShouldContain("BtnSend");

        // 2. Verifica destino Obsidian Vault
        var hiddenVaultJsonl = Path.Combine(_tempVaultDir, ".synapse", "logs", "synapse-activity.jsonl");
        File.Exists(hiddenVaultJsonl).ShouldBeTrue();

        var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
        var visibleVaultMarkdown = Path.Combine(_tempVaultDir, "Synapse", "Logs", $"Registro de Atividades — {todayStr}.md");
        File.Exists(visibleVaultMarkdown).ShouldBeTrue();

        var mdContent = await File.ReadAllTextAsync(visibleVaultMarkdown);
        mdContent.ShouldContain("# 📊 Registro de Atividades");
        mdContent.ShouldContain("`ChatVault`");
        mdContent.ShouldContain("**UserClick**");
    }

    [Fact]
    public async Task LogChatAsync_ShouldRecordQuestionAnswerDurationAndAppVersion()
    {
        var logger = new SynapseActivityLogger(_tempLocalLogsDir, _tempVaultDir);

        await logger.LogChatAsync(
            question: "Quem são meus amigos?",
            answer: "Seu amigo é o [[Felipe]].",
            durationMs: 1450,
            status: "Success",
            notesConsulted: ["Lista de Amigos"],
            savedNotePath: "Pessoas/Lista de Amigos.md");

        var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
        var visibleVaultMarkdown = Path.Combine(_tempVaultDir, "Synapse", "Logs", $"Registro de Atividades — {todayStr}.md");
        var mdContent = await File.ReadAllTextAsync(visibleVaultMarkdown);

        mdContent.ShouldContain("Quem são meus amigos?");
        mdContent.ShouldContain("Seu amigo é o [[Felipe]].");
        mdContent.ShouldContain("1450ms");
        mdContent.ShouldContain("✅ Sucesso");

        var logFile = Path.Combine(_tempLocalLogsDir, "synapse_activity.log");
        var logContent = await File.ReadAllTextAsync(logFile);
        logContent.ShouldContain("Q: \"Quem são meus amigos?\"");
        logContent.ShouldContain("A: \"Seu amigo é o [[Felipe]].\"");
        logContent.ShouldContain("[1450ms]");
    }

    [Fact]
    public async Task TrackOperationAsync_WhenOperationExceedsTimeout_ShouldLogTimeout()
    {
        var logger = new SynapseActivityLogger(_tempLocalLogsDir, _tempVaultDir);

        await Should.ThrowAsync<TimeoutException>(async () =>
        {
            await logger.TrackOperationAsync(
                "BrainEngine",
                "LongRunningQuery",
                "Teste de Timeout",
                async ct =>
                {
                    await Task.Delay(10000, ct);
                    return "ok";
                },
                timeoutMs: 100); // Timeout forçado de 100ms para teste (margem grande p/ evitar flakiness em CI sob carga)
        });

        var jsonlFile = Path.Combine(_tempLocalLogsDir, "synapse_activity.jsonl");
        var jsonlContent = await File.ReadAllTextAsync(jsonlFile);
        jsonlContent.ShouldContain("\"status\":\"Timeout\"");
        jsonlContent.ShouldContain("A operação excedeu o tempo limite");
    }

    [Fact]
    public async Task LogLiveToUserVault_IfVaultExists_ShouldWriteRealMarkdownLog()
    {
        var realVault = @"C:\Users\victo\Repos\Pessoal\Obsidian\Vault\TEST";
        if (!Directory.Exists(realVault)) return;

        var logger = SynapseActivityLogger.Instance;
        logger.SetVaultPath(realVault);

        await logger.LogClickAsync("TrayMenu", "OpenChatVault", "Usuário abriu a janela de chat");
        await logger.LogClickAsync("ChatVault", "BtnSend", "Pergunta: 'quem são meus amigos?'");
        await logger.LogChatAsync(
            "quem são meus amigos?",
            "Com base nas notas do seu cofre, o amigo registrado é o [[Felipe]] na nota [[Lista de Amigos]].",
            1280,
            "Success",
            ["Lista de Amigos"],
            "Pessoas/Lista de Amigos.md");

        var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
        var vaultMarkdown = Path.Combine(realVault, "Synapse", "Logs", $"Registro de Atividades — {todayStr}.md");
        File.Exists(vaultMarkdown).ShouldBeTrue();
    }
}
