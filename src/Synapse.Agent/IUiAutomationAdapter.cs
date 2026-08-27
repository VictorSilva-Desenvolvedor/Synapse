namespace Synapse.Agent;

/// <summary>
/// Abstração para operações de UI Automation no sistema operacional.
/// Permite injeção de dublês de teste para execução sem interface gráfica em CI.
/// </summary>
public interface IUiAutomationAdapter
{
    bool TryFindAndFocusWindow(string processName);
    bool TrySendText(string processName, string text);
    bool TryClickElement(string processName, string elementName);
}
