using Synapse.Agent;
using Synapse.Agent.Models;
using Xunit;

namespace Synapse.Tests.Agent;

/// <summary>
/// O portao de autorizacao so funciona se descrever o comando com precisao. Estes testes
/// travam duas coisas: que os SEIS tipos tem descricao (antes, quatro caiam num fallback
/// mudo) e que as chaves de payload batem com as que o RemoteCommandExecutor le.
/// </summary>
public sealed class RemoteCommandDescriberTests
{
    private static RemoteCommand Cmd(RemoteCommandType type, Dictionary<string, string> payload)
        => new(Guid.NewGuid(), DateTimeOffset.UtcNow, type, payload, "Dispositivo-Teste");

    [Theory]
    [InlineData(RemoteCommandType.OpenNote, RemoteCommandRisk.Low)]
    [InlineData(RemoteCommandType.FocusWindow, RemoteCommandRisk.Low)]
    [InlineData(RemoteCommandType.OpenApp, RemoteCommandRisk.Medium)]
    [InlineData(RemoteCommandType.AskVault, RemoteCommandRisk.Medium)]
    [InlineData(RemoteCommandType.TypeText, RemoteCommandRisk.High)]
    [InlineData(RemoteCommandType.ClickElement, RemoteCommandRisk.High)]
    public void Describe_ClassificaORiscoDeCadaTipo(RemoteCommandType type, RemoteCommandRisk esperado)
    {
        var d = RemoteCommandDescriber.Describe(Cmd(type, []));
        Assert.Equal(esperado, d.Risk);
    }

    [Fact]
    public void Describe_TodosOsTiposTemAcaoLegivel()
    {
        // O fallback antigo imprimia so "Tipo: OpenApp". Nenhum tipo pode voltar a isso.
        foreach (var type in Enum.GetValues<RemoteCommandType>())
        {
            var d = RemoteCommandDescriber.Describe(Cmd(type, []));

            Assert.False(string.IsNullOrWhiteSpace(d.Action), $"{type} sem acao legivel");
            Assert.False(string.IsNullOrWhiteSpace(d.RiskReason), $"{type} sem motivo do risco");
            Assert.DoesNotContain("Tipo:", d.Action, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Describe_TypeText_UsaAsChavesReaisDoExecutor()
    {
        var d = RemoteCommandDescriber.Describe(Cmd(
            RemoteCommandType.TypeText,
            new Dictionary<string, string> { ["processName"] = "Obsidian", ["text"] = "ola mundo" }));

        Assert.Equal("Obsidian", d.Target);
        Assert.Equal("ola mundo", d.Payload);
    }

    [Fact]
    public void Describe_OpenApp_UsaChaveApp()
    {
        var d = RemoteCommandDescriber.Describe(Cmd(
            RemoteCommandType.OpenApp,
            new Dictionary<string, string> { ["app"] = "notepad" }));

        Assert.Equal("notepad", d.Target);
    }

    [Fact]
    public void Describe_OpenNote_UsaChaveRelativePath()
    {
        var d = RemoteCommandDescriber.Describe(Cmd(
            RemoteCommandType.OpenNote,
            new Dictionary<string, string> { ["relativePath"] = "Notas/Arquitetura.md" }));

        Assert.Equal("Notas/Arquitetura.md", d.Target);
    }

    [Fact]
    public void Describe_AskVault_UsaChaveQuestion()
    {
        var d = RemoteCommandDescriber.Describe(Cmd(
            RemoteCommandType.AskVault,
            new Dictionary<string, string> { ["question"] = "o que e SM-2?" }));

        Assert.Equal("o que e SM-2?", d.Payload);
    }

    [Fact]
    public void Describe_PayloadAusente_NaoQuebra()
    {
        var d = RemoteCommandDescriber.Describe(Cmd(RemoteCommandType.TypeText, []));

        Assert.Null(d.Target);
        Assert.Null(d.Payload);
        Assert.Equal(RemoteCommandRisk.High, d.Risk);
    }
}
