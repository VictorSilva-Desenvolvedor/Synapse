using Synapse.Agent.Models;

namespace Synapse.Agent;

/// <summary>Quanto poder o comando exerce sobre a maquina do usuario.</summary>
public enum RemoteCommandRisk
{
    Low,
    Medium,
    High
}

/// <summary>
/// Um comando remoto traduzido para linguagem humana, pronto para a tela de autorizacao.
/// </summary>
public sealed record RemoteCommandDescription(
    RemoteCommandRisk Risk,
    string RiskLabel,
    string RiskReason,
    string Action,
    string? Target,
    string? Payload);

/// <summary>
/// Classifica e descreve comandos remotos para o portao de autorizacao humana.
///
/// Vive no Synapse.Agent, nao na UI, por dois motivos: e conhecimento sobre comandos
/// (nao sobre pixels) e assim pode ser testado sem WPF.
///
/// A versao anterior desta descricao so tratava TypeText e ClickElement — os outros
/// quatro tipos caiam num fallback que imprimia apenas "Tipo: OpenApp", pedindo que o
/// usuario autorizasse abrir um programa sem dizer qual. As chaves de payload aqui sao
/// as mesmas que o RemoteCommandExecutor le de verdade.
/// </summary>
public static class RemoteCommandDescriber
{
    public static RemoteCommandDescription Describe(RemoteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payload = command.Payload;

        return command.Type switch
        {
            RemoteCommandType.OpenNote => new RemoteCommandDescription(
                RemoteCommandRisk.Low,
                "RISCO BAIXO",
                "so abre uma nota que ja esta no seu cofre",
                "Abrir uma nota do cofre",
                Get(payload, "relativePath"),
                null),

            RemoteCommandType.FocusWindow => new RemoteCommandDescription(
                RemoteCommandRisk.Low,
                "RISCO BAIXO",
                "so traz uma janela para a frente",
                "Trazer uma janela para a frente",
                Get(payload, "processName"),
                null),

            RemoteCommandType.OpenApp => new RemoteCommandDescription(
                RemoteCommandRisk.Medium,
                "RISCO MEDIO",
                "inicia um programa no seu computador",
                "Abrir um programa",
                Get(payload, "app"),
                null),

            // ProcessChatTurnAsync pode gravar uma nota nova (SavedNotePath) — depende
            // do que a IA decidir. "Pode", nao "vai": o prompt nao deve afirmar demais.
            RemoteCommandType.AskVault => new RemoteCommandDescription(
                RemoteCommandRisk.Medium,
                "RISCO MEDIO",
                "consulta o cofre e pode gravar uma nota nova",
                "Perguntar ao cofre",
                null,
                Get(payload, "question")),

            RemoteCommandType.TypeText => new RemoteCommandDescription(
                RemoteCommandRisk.High,
                "RISCO ALTO",
                "vai digitar no seu teclado, como se fosse voce",
                "Digitar texto",
                Get(payload, "processName"),
                Get(payload, "text")),

            RemoteCommandType.ClickElement => new RemoteCommandDescription(
                RemoteCommandRisk.High,
                "RISCO ALTO",
                "vai clicar na interface, como se fosse voce",
                "Clicar em um elemento da tela",
                Get(payload, "processName"),
                Get(payload, "elementName")),

            // Tipo novo ainda nao classificado: trata como ALTO ate alguem decidir.
            // O default seguro num portao de seguranca e sempre o mais restritivo.
            _ => new RemoteCommandDescription(
                RemoteCommandRisk.High,
                "RISCO DESCONHECIDO",
                "tipo de comando nao reconhecido por esta versao",
                command.Type.ToString(),
                null,
                null)
        };
    }

    private static string? Get(IReadOnlyDictionary<string, string>? payload, string key)
        => payload is not null && payload.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}
