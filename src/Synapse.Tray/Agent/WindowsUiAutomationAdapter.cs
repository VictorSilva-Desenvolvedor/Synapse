using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;
using Synapse.Agent;

namespace Synapse.Tray.Agent;

/// <summary>
/// Implementação real de UI Automation e entrada de teclado para Windows.
/// </summary>
public sealed class WindowsUiAutomationAdapter : IUiAutomationAdapter
{
    private readonly ILogger<WindowsUiAutomationAdapter>? _logger;

    public WindowsUiAutomationAdapter(ILogger<WindowsUiAutomationAdapter>? logger = null)
    {
        _logger = logger;
    }

    public bool TryFindAndFocusWindow(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        var cleanName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        try
        {
            var processes = Process.GetProcessesByName(cleanName);
            foreach (var proc in processes)
            {
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(proc.MainWindowHandle);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Falha ao buscar/focar janela do processo '{ProcessName}'", cleanName);
        }

        return false;
    }

    public bool TrySendText(string processName, string text)
    {
        if (string.IsNullOrEmpty(text)) return true;

        if (!TryFindAndFocusWindow(processName))
        {
            _logger?.LogWarning("Não foi possível focar a janela do processo '{ProcessName}' para envio de texto.", processName);
            return false;
        }

        // Aguarda estabilização do foco
        Thread.Sleep(80);

        try
        {
            var inputs = new List<INPUT>(text.Length * 2);

            foreach (var ch in text)
            {
                // Key Down (Unicode)
                var down = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = ch,
                            dwFlags = KEYEVENTF_UNICODE,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };

                // Key Up (Unicode)
                var up = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = ch,
                            dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };

                inputs.Add(down);
                inputs.Add(up);
            }

            var inputArray = inputs.ToArray();
            var sent = SendInput((uint)inputArray.Length, inputArray, Marshal.SizeOf(typeof(INPUT)));
            return sent == inputArray.Length;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro ao enviar texto via SendInput para o processo '{ProcessName}'", processName);
            return false;
        }
    }

    public bool TryClickElement(string processName, string elementName)
    {
        if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(elementName)) return false;

        var cleanName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        try
        {
            var processes = Process.GetProcessesByName(cleanName);
            var targetProc = processes.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            if (targetProc == null)
            {
                _logger?.LogWarning("Nenhuma janela visível encontrada para o processo '{ProcessName}'", cleanName);
                return false;
            }

            ShowWindow(targetProc.MainWindowHandle, SW_RESTORE);
            SetForegroundWindow(targetProc.MainWindowHandle);
            Thread.Sleep(80);

            var rootElement = AutomationElement.FromHandle(targetProc.MainWindowHandle);
            if (rootElement == null)
            {
                _logger?.LogWarning("Não foi possível obter AutomationElement raiz para o processo '{ProcessName}'", cleanName);
                return false;
            }

            // 1. Busca direta por NameProperty
            var nameCondition = new PropertyCondition(AutomationElement.NameProperty, elementName, PropertyConditionFlags.IgnoreCase);
            var element = rootElement.FindFirst(TreeScope.Descendants, nameCondition);

            // 2. Fallback: varredura em árvore
            if (element == null)
            {
                var allDescendants = rootElement.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                foreach (AutomationElement candidate in allDescendants)
                {
                    try
                    {
                        if (string.Equals(candidate.Current.Name, elementName, StringComparison.OrdinalIgnoreCase))
                        {
                            element = candidate;
                            break;
                        }
                    }
                    catch
                    {
                        // Elemento pode ter sido reciclado
                    }
                }
            }

            if (element == null)
            {
                _logger?.LogWarning("Elemento '{ElementName}' não encontrado na árvore de UI do processo '{ProcessName}'", elementName, cleanName);
                return false;
            }

            // Invocação por padrões suportados
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObj) && invokeObj is InvokePattern invokePattern)
            {
                invokePattern.Invoke();
                return true;
            }

            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var toggleObj) && toggleObj is TogglePattern togglePattern)
            {
                togglePattern.Toggle();
                return true;
            }

            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selObj) && selObj is SelectionItemPattern selPattern)
            {
                selPattern.Select();
                return true;
            }

            _logger?.LogWarning("Elemento '{ElementName}' encontrado mas não suporta InvokePattern/TogglePattern/SelectionItemPattern.", elementName);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro ao tentar clicar no elemento '{ElementName}' do processo '{ProcessName}'", elementName, cleanName);
            return false;
        }
    }

    #region Win32 P/Invoke

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    private const int SW_RESTORE = 9;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    #endregion
}
