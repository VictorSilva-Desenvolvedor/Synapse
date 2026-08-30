using NSubstitute;
using Shouldly;
using Synapse.Agent;
using Synapse.Agent.Models;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;
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
    public async Task ExecuteAsync_OpenApp_WhenNaturalLanguagePhraseGiven_ShouldResolveAllowedAppKey()
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
            Payload: new Dictionary<string, string> { ["app"] = "abre o calc por favor" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        // Como calc.exe é um executável de sistema do Windows, a execução terá sucesso
        result.Status.ShouldBe(RemoteCommandStatus.Success);
        result.Message.ShouldContain("calc");
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

    #region Fase 2: TypeText Tests

    [Fact]
    public async Task ExecuteAsync_TypeText_WhenMissingPayload_ShouldReject()
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
            Type: RemoteCommandType.TypeText,
            Payload: new Dictionary<string, string> { ["processName"] = "notepad" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("obrigatórios");
    }

    [Fact]
    public async Task ExecuteAsync_TypeText_WhenProcessNotInAllowlist_ShouldRejectWithoutPromptOrUi()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            RemoteAllowedApps = new Dictionary<string, string>
            {
                ["obsidian"] = "Obsidian.exe"
            }
        };

        var mockPrompt = Substitute.For<IRemoteConfirmationPrompt>();
        var mockUi = Substitute.For<IUiAutomationAdapter>();

        var executor = new RemoteCommandExecutor(config, mockPrompt, mockUi);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.TypeText,
            Payload: new Dictionary<string, string>
            {
                ["processName"] = "cmd",
                ["text"] = "calc.exe"
            },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("não está na lista de aplicativos permitidos");

        await mockPrompt.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default, default);
        mockUi.DidNotReceiveWithAnyArgs().TrySendText(default!, default!);
    }

    [Fact]
    public async Task ExecuteAsync_TypeText_WhenConfirmationPromptIsNull_ShouldRejectNeverAutoApprove()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            RemoteAllowedApps = new Dictionary<string, string>
            {
                ["notepad"] = "notepad.exe"
            }
        };

        var mockUi = Substitute.For<IUiAutomationAdapter>();
        var executor = new RemoteCommandExecutor(config, confirmationPrompt: null, uiAutomation: mockUi);

        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.TypeText,
            Payload: new Dictionary<string, string>
            {
                ["processName"] = "notepad",
                ["text"] = "Hello World"
            },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("nenhum mecanismo de confirmação");
        mockUi.DidNotReceiveWithAnyArgs().TrySendText(default!, default!);
    }

    [Fact]
    public async Task ExecuteAsync_TypeText_WhenConfirmationDenied_ShouldRejectWithoutExecutingUi()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            RemoteAllowedApps = new Dictionary<string, string>
            {
                ["notepad"] = "notepad.exe"
            }
        };

        var mockPrompt = Substitute.For<IRemoteConfirmationPrompt>();
        mockPrompt.ConfirmAsync(Arg.Any<RemoteCommand>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var mockUi = Substitute.For<IUiAutomationAdapter>();

        var executor = new RemoteCommandExecutor(config, mockPrompt, mockUi);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.TypeText,
            Payload: new Dictionary<string, string>
            {
                ["processName"] = "notepad",
                ["text"] = "Hello World"
            },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("não confirmada");
        mockUi.DidNotReceiveWithAnyArgs().TrySendText(default!, default!);
    }

    [Fact]
    public async Task ExecuteAsync_TypeText_WhenConfirmationApproved_ShouldCallUiAutomationAndReturnSuccess()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            RemoteAllowedApps = new Dictionary<string, string>
            {
                ["notepad"] = "C:\\Windows\\notepad.exe"
            }
        };

        var mockPrompt = Substitute.For<IRemoteConfirmationPrompt>();
        mockPrompt.ConfirmAsync(Arg.Any<RemoteCommand>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var mockUi = Substitute.For<IUiAutomationAdapter>();
        mockUi.TrySendText("notepad", "Texto Digitado").Returns(true);

        var executor = new RemoteCommandExecutor(config, mockPrompt, mockUi);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.TypeText,
            Payload: new Dictionary<string, string>
            {
                ["processName"] = "notepad.exe",
                ["text"] = "Texto Digitado"
            },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Success);
        result.Message.ShouldContain("Texto digitado com sucesso");
        mockUi.Received(1).TrySendText("notepad", "Texto Digitado");
    }

    #endregion

    #region Fase 2: ClickElement Tests

    [Fact]
    public async Task ExecuteAsync_ClickElement_WhenMissingPayload_ShouldReject()
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
            Type: RemoteCommandType.ClickElement,
            Payload: new Dictionary<string, string> { ["processName"] = "obsidian" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("obrigatórios");
    }

    [Fact]
    public async Task ExecuteAsync_ClickElement_WhenProcessNotInAllowlist_ShouldRejectWithoutPromptOrUi()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            RemoteAllowedApps = new Dictionary<string, string>
            {
                ["obsidian"] = "Obsidian.exe"
            }
        };

        var mockPrompt = Substitute.For<IRemoteConfirmationPrompt>();
        var mockUi = Substitute.For<IUiAutomationAdapter>();

        var executor = new RemoteCommandExecutor(config, mockPrompt, mockUi);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.ClickElement,
            Payload: new Dictionary<string, string>
            {
                ["processName"] = "explorer",
                ["elementName"] = "Fechar"
            },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("não está na lista de aplicativos permitidos");

        await mockPrompt.DidNotReceiveWithAnyArgs().ConfirmAsync(default!, default, default);
        mockUi.DidNotReceiveWithAnyArgs().TryClickElement(default!, default!);
    }

    [Fact]
    public async Task ExecuteAsync_ClickElement_WhenConfirmationPromptIsNull_ShouldRejectNeverAutoApprove()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            RemoteAllowedApps = new Dictionary<string, string>
            {
                ["obsidian"] = "Obsidian.exe"
            }
        };

        var mockUi = Substitute.For<IUiAutomationAdapter>();
        var executor = new RemoteCommandExecutor(config, confirmationPrompt: null, uiAutomation: mockUi);

        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.ClickElement,
            Payload: new Dictionary<string, string>
            {
                ["processName"] = "obsidian",
                ["elementName"] = "Sync Now"
            },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("nenhum mecanismo de confirmação");
        mockUi.DidNotReceiveWithAnyArgs().TryClickElement(default!, default!);
    }

    [Fact]
    public async Task ExecuteAsync_ClickElement_WhenConfirmationDenied_ShouldRejectWithoutExecutingUi()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            RemoteAllowedApps = new Dictionary<string, string>
            {
                ["obsidian"] = "Obsidian.exe"
            }
        };

        var mockPrompt = Substitute.For<IRemoteConfirmationPrompt>();
        mockPrompt.ConfirmAsync(Arg.Any<RemoteCommand>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var mockUi = Substitute.For<IUiAutomationAdapter>();

        var executor = new RemoteCommandExecutor(config, mockPrompt, mockUi);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.ClickElement,
            Payload: new Dictionary<string, string>
            {
                ["processName"] = "obsidian",
                ["elementName"] = "Sync Now"
            },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("não confirmada");
        mockUi.DidNotReceiveWithAnyArgs().TryClickElement(default!, default!);
    }

    [Fact]
    public async Task ExecuteAsync_ClickElement_WhenConfirmationApproved_ShouldCallUiAutomationAndReturnSuccess()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            RemoteAllowedApps = new Dictionary<string, string>
            {
                ["obsidian"] = "C:\\Users\\AppData\\Obsidian.exe"
            }
        };

        var mockPrompt = Substitute.For<IRemoteConfirmationPrompt>();
        mockPrompt.ConfirmAsync(Arg.Any<RemoteCommand>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var mockUi = Substitute.For<IUiAutomationAdapter>();
        mockUi.TryClickElement("obsidian", "Salvar").Returns(true);

        var executor = new RemoteCommandExecutor(config, mockPrompt, mockUi);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.ClickElement,
            Payload: new Dictionary<string, string>
            {
                ["processName"] = "obsidian",
                ["elementName"] = "Salvar"
            },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Success);
        result.Message.ShouldContain("clicado com sucesso");
        mockUi.Received(1).TryClickElement("obsidian", "Salvar");
    }

    #endregion

    #region Fase 4: AskVault (RAG Query)

    [Fact]
    public async Task ExecuteAsync_AskVault_WhenRemoteControlDisabled_ShouldRejectWithoutCallingBrain()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = false,
            VaultPath = _tempVaultDir,
            GeminiApiKey = "fake-key"
        };

        var mockBrain = Substitute.For<IVaultBrainQuery>();
        var executor = new RemoteCommandExecutor(config, brainQuery: mockBrain);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.AskVault,
            Payload: new Dictionary<string, string> { ["question"] = "Como funciona o projeto?" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("desativado");
        await mockBrain.DidNotReceiveWithAnyArgs().ProcessChatTurnAsync(default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_AskVault_WhenQuestionMissingOrEmpty_ShouldReject()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            GeminiApiKey = "fake-key"
        };

        var mockBrain = Substitute.For<IVaultBrainQuery>();
        var executor = new RemoteCommandExecutor(config, brainQuery: mockBrain);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.AskVault,
            Payload: new Dictionary<string, string>(),
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("question");
        await mockBrain.DidNotReceiveWithAnyArgs().ProcessChatTurnAsync(default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_AskVault_WhenGeminiApiKeyMissingButBrainQueryAvailable_ShouldStillAnswer()
    {
        // Sem chave Gemini nao significa "sem IA": o fallback automatico (BrainProviderFactory)
        // pode ter montado o brainQuery so com Ollama local. A ausencia de GeminiApiKey sozinha
        // nao deve mais rejeitar o comando - so a ausencia de brainQuery em si (ver teste abaixo).
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            GeminiApiKey = ""
        };

        var mockBrain = Substitute.For<IVaultBrainQuery>();
        mockBrain.ProcessChatTurnAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ChatTurnOutcome("Resposta via Ollama local.", null, []));
        var executor = new RemoteCommandExecutor(config, brainQuery: mockBrain);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.AskVault,
            Payload: new Dictionary<string, string> { ["question"] = "Qual é o plano?" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Success);
    }

    [Fact]
    public async Task ExecuteAsync_AskVault_WhenBrainQueryIsNull_ShouldRejectSafely()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            GeminiApiKey = "fake-key"
        };

        var executor = new RemoteCommandExecutor(config, brainQuery: null);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.AskVault,
            Payload: new Dictionary<string, string> { ["question"] = "Onde estão as notas?" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("não configurada");
    }

    [Fact]
    public async Task ExecuteAsync_AskVault_WhenVaultPathDoesNotExist_ShouldReject()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = Path.Combine(Path.GetTempPath(), $"non-existent-{Guid.NewGuid():N}"),
            GeminiApiKey = "fake-key"
        };

        var mockBrain = Substitute.For<IVaultBrainQuery>();
        var executor = new RemoteCommandExecutor(config, brainQuery: mockBrain);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.AskVault,
            Payload: new Dictionary<string, string> { ["question"] = "Onde estão as notas?" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Rejected);
        result.Message.ShouldContain("Cofre local não configurado ou diretório não encontrado");
        await mockBrain.DidNotReceiveWithAnyArgs().ProcessChatTurnAsync(default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_AskVault_WhenSuccessWithSources_ShouldReturnAnswerWithSourcesWikilinks()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            GeminiApiKey = "fake-key"
        };

        var mockBrain = Substitute.For<IVaultBrainQuery>();
        mockBrain.ProcessChatTurnAsync("Como funciona a arquitetura?", _tempVaultDir, Arg.Any<CancellationToken>())
            .Returns(new ChatTurnOutcome(
                "O Synapse utiliza Clean Architecture com C# .NET 8 e protocolo GitHub Relay.",
                null,
                [
                    new SemanticSearchResult("Projetos/Arquitetura.md", "Arquitetura", "", 0.9f),
                    new SemanticSearchResult("Brain/Decisoes.md", "Decisoes", "", 0.85f)
                ]));

        var executor = new RemoteCommandExecutor(config, brainQuery: mockBrain);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.AskVault,
            Payload: new Dictionary<string, string> { ["question"] = "Como funciona a arquitetura?" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Success);
        result.Message.ShouldContain("O Synapse utiliza Clean Architecture");
        result.Message.ShouldContain("Fontes: [[Arquitetura]], [[Decisoes]]");
    }

    [Fact]
    public async Task ExecuteAsync_AskVault_WhenSuccessWithoutSources_ShouldReturnAnswerWithoutSources()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            GeminiApiKey = "fake-key"
        };

        var mockBrain = Substitute.For<IVaultBrainQuery>();
        mockBrain.ProcessChatTurnAsync("Existe alguma anotação sobre Rust?", _tempVaultDir, Arg.Any<CancellationToken>())
            .Returns(new ChatTurnOutcome(
                "Não encontrei notas relevantes no seu cofre para responder a essa pergunta.",
                null,
                []));

        var executor = new RemoteCommandExecutor(config, brainQuery: mockBrain);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.AskVault,
            Payload: new Dictionary<string, string> { ["question"] = "Existe alguma anotação sobre Rust?" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Success);
        result.Message.ShouldBe("Não encontrei notas relevantes no seu cofre para responder a essa pergunta.");
        result.Message.ShouldNotContain("Fontes:");
        result.Message.ShouldNotContain("Salvo em:");
    }

    [Fact]
    public async Task ExecuteAsync_AskVault_WhenCaptureTurn_ShouldReturnMessageWithSavedNoteWikilink()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            GeminiApiKey = "fake-key"
        };

        var mockBrain = Substitute.For<IVaultBrainQuery>();
        mockBrain.ProcessChatTurnAsync("lembrete: comprar café", _tempVaultDir, Arg.Any<CancellationToken>())
            .Returns(new ChatTurnOutcome(
                "Anotado! Capturei seu lembrete no cofre.",
                "Brain/Tarefas/Comprar Café.md",
                []));

        var executor = new RemoteCommandExecutor(config, brainQuery: mockBrain);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.AskVault,
            Payload: new Dictionary<string, string> { ["question"] = "lembrete: comprar café" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Success);
        result.Message.ShouldContain("Anotado! Capturei seu lembrete no cofre.");
        result.Message.ShouldContain("💾 Salvo em: [[Comprar Café]]");
        result.Message.ShouldNotContain("Fontes:");
    }

    [Fact]
    public async Task ExecuteAsync_AskVault_WhenHybridTurn_ShouldReturnMessageWithSavedNoteAndSources()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            GeminiApiKey = "fake-key"
        };

        var mockBrain = Substitute.For<IVaultBrainQuery>();
        mockBrain.ProcessChatTurnAsync("conectar ideia X com Arquitetura", _tempVaultDir, Arg.Any<CancellationToken>())
            .Returns(new ChatTurnOutcome(
                "Nota criada e conectada com sucesso.",
                "Brain/Conceitos/Ideia X.md",
                [new SemanticSearchResult("Projetos/Arquitetura.md", "Arquitetura", "", 0.88f)]));

        var executor = new RemoteCommandExecutor(config, brainQuery: mockBrain);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.AskVault,
            Payload: new Dictionary<string, string> { ["question"] = "conectar ideia X com Arquitetura" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Success);
        result.Message.ShouldContain("Nota criada e conectada com sucesso.");
        result.Message.ShouldContain("💾 Salvo em: [[Ideia X]]");
        result.Message.ShouldContain("Fontes: [[Arquitetura]]");
    }

    [Fact]
    public async Task ExecuteAsync_AskVault_WithRealVaultRagEngine_WhenCapturePrompt_ShouldCreateNoteInVaultAndConfirm()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            GeminiApiKey = "fake-key"
        };

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.5f, 0.5f, 0.5f }));

        var mockAi = Substitute.For<IBrainAiProvider>();
        mockAi.ProcessChatTurnAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<IReadOnlyList<SemanticSearchResult>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatTurnResult
            {
                ShouldCapture = true,
                Title = "Reunião Amanhã às 10h",
                Category = "Tarefas",
                Tags = ["reuniao", "trabalho"],
                BodyMarkdown = "Alinhar pauta da sprint na reunião das 10h.",
                KeyPoints = ["Reunião 10h", "Pauta da sprint"],
                SuggestedConnections = [],
                ShouldAnswer = false,
                ReplyMessage = "Lembrete salvo no cofre com sucesso!"
            }));

        var brainConfig = new BrainConfig { DefaultFolder = "Brain", AutoCategorizeFolders = true };
        var realRagEngine = new VaultRagEngine(mockEmbedding, mockAi, brainConfig);

        var executor = new RemoteCommandExecutor(config, brainQuery: realRagEngine);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.AskVault,
            Payload: new Dictionary<string, string> { ["question"] = "lembrete: reuniao amanha as 10h" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Success);
        result.Message.ShouldContain("Lembrete salvo no cofre com sucesso!");
        result.Message.ShouldContain("💾 Salvo em: [[Reunião Amanhã às 10h]]");

        var createdFiles = Directory.GetFiles(_tempVaultDir, "*.md", SearchOption.AllDirectories);
        createdFiles.Length.ShouldBe(1);
        var fileContent = await File.ReadAllTextAsync(createdFiles[0]);
        fileContent.ShouldContain("titulo: \"Reunião Amanhã às 10h\"");
        fileContent.ShouldContain("categoria: \"Tarefas\"");
        fileContent.ShouldContain("Alinhar pauta da sprint na reunião das 10h.");
    }

    [Fact]
    public async Task ExecuteAsync_AskVault_WhenBrainQueryThrows_ShouldReturnFailed()
    {
        var config = new SynapseConfig
        {
            RemoteControlEnabled = true,
            VaultPath = _tempVaultDir,
            GeminiApiKey = "fake-key"
        };

        var mockBrain = Substitute.For<IVaultBrainQuery>();
        mockBrain.ProcessChatTurnAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatTurnOutcome>>(_ => Task.FromException<ChatTurnOutcome>(new HttpRequestException("Erro de conexão com a API do Gemini.")));

        var executor = new RemoteCommandExecutor(config, brainQuery: mockBrain);
        var command = new RemoteCommand(
            Id: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Type: RemoteCommandType.AskVault,
            Payload: new Dictionary<string, string> { ["question"] = "Qual é o resumo?" },
            RequestedBy: "mobile-user");

        var result = await executor.ExecuteAsync(command);

        result.Status.ShouldBe(RemoteCommandStatus.Failed);
        result.Message.ShouldContain("Falha ao consultar o cofre");
        result.Message.ShouldContain("Erro de conexão");
    }

    #endregion

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }
}
