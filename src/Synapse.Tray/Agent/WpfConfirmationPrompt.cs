using System.Windows;
using System.Windows.Threading;
using Synapse.Agent;
using Synapse.Agent.Models;

namespace Synapse.Tray.Agent;

/// <summary>
/// Confirmacao humana interativa de comandos remotos, em WPF.
/// Substitui WinFormsConfirmationPrompt.
///
/// SEGURANCA: qualquer falha em exibir o dialogo resolve como NEGADO. Nunca
/// troque isso por um fallback permissivo - um erro de UI nao pode virar um "sim".
/// </summary>
public sealed class WpfConfirmationPrompt : IRemoteConfirmationPrompt
{
    private readonly Dispatcher _dispatcher;

    public WpfConfirmationPrompt(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher
                      ?? Application.Current?.Dispatcher
                      ?? Dispatcher.CurrentDispatcher;
    }

    public Task<bool> ConfirmAsync(RemoteCommand command, TimeSpan timeout, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                var window = new RemoteConfirmationWindow(command, timeout, ct);
                window.Closed += (_, _) => tcs.TrySetResult(window.IsApproved);
                window.Show();
                window.Activate();
            }
            catch (Exception)
            {
                // Nao foi possivel perguntar ao humano: nega.
                tcs.TrySetResult(false);
            }
        });

        return tcs.Task;
    }
}
