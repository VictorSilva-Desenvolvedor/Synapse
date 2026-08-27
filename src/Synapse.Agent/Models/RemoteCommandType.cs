namespace Synapse.Agent.Models;

/// <summary>
/// Tipos de comandos remotos suportados pelo agente (Fases 1, 2 e 4).
/// </summary>
public enum RemoteCommandType
{
    OpenApp,
    OpenNote,
    FocusWindow,
    TypeText,
    ClickElement,
    AskVault
}
