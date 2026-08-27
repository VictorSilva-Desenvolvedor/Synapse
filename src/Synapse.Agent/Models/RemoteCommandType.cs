namespace Synapse.Agent.Models;

/// <summary>
/// Tipos de comandos remotos suportados pelo agente (Fase 1 e Fase 2).
/// </summary>
public enum RemoteCommandType
{
    OpenApp,
    OpenNote,
    FocusWindow,
    TypeText,
    ClickElement
}
