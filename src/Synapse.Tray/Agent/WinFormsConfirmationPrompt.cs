using Synapse.Agent;
using Synapse.Agent.Models;
using Timer = System.Windows.Forms.Timer;

namespace Synapse.Tray.Agent;

/// <summary>
/// Implementação de confirmação humana interativa utilizando Windows Forms na UI thread.
/// Mostra diálogo com detalhes do comando, timeout decrescente e negação por padrão.
/// </summary>
public sealed class WinFormsConfirmationPrompt : IRemoteConfirmationPrompt
{
    private readonly SynchronizationContext? _syncContext;

    public WinFormsConfirmationPrompt(SynchronizationContext? syncContext = null)
    {
        _syncContext = syncContext ?? SynchronizationContext.Current;
    }

    public Task<bool> ConfirmAsync(RemoteCommand command, TimeSpan timeout, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void ShowForm()
        {
            try
            {
                var form = new RemoteConfirmationForm(command, timeout, ct);

                form.FormClosed += (_, _) =>
                {
                    tcs.TrySetResult(form.IsApproved);
                    form.Dispose();
                };

                // Registra cancelamento
                ct.Register(() =>
                {
                    try
                    {
                        if (!form.IsDisposed && form.IsHandleCreated)
                        {
                            form.BeginInvoke(new Action(() =>
                            {
                                if (!form.IsDisposed) form.Close();
                            }));
                        }
                    }
                    catch { }
                    tcs.TrySetResult(false);
                });

                form.Show();
                form.BringToFront();
                form.Activate();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        if (_syncContext != null)
        {
            _syncContext.Post(_ => ShowForm(), null);
        }
        else
        {
            // Fallback caso não haja SynchronizationContext capturado
            var thread = new Thread(() =>
            {
                ShowForm();
                Application.Run();
            })
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        return tcs.Task;
    }

    private sealed class RemoteConfirmationForm : Form
    {
        private readonly Timer _countdownTimer;
        private int _secondsRemaining;
        private readonly Label _timerLabel;

        public bool IsApproved { get; private set; }

        public RemoteConfirmationForm(RemoteCommand command, TimeSpan timeout, CancellationToken ct)
        {
            _secondsRemaining = (int)Math.Max(1, timeout.TotalSeconds);

            Text = "Synapse Remote — Solicitação de Ação Sensível";
            Width = 520;
            Height = 320;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;

            var titleLabel = new Label
            {
                Text = "Autorização de Controle Remoto",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(20, 15),
                AutoSize = true
            };

            var description = GetCommandDescription(command);
            var descTextBox = new TextBox
            {
                Text = description,
                Location = new Point(20, 50),
                Width = 460,
                Height = 140,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window
            };

            _timerLabel = new Label
            {
                Text = $"Tempo restante para resposta: {_secondsRemaining}s (negará automaticamente)",
                Location = new Point(20, 205),
                Width = 460,
                ForeColor = Color.DarkRed,
                Font = new Font("Segoe UI", 9, FontStyle.Italic)
            };

            var btnDeny = new Button
            {
                Text = "Negar (Não)",
                DialogResult = DialogResult.No,
                Location = new Point(260, 235),
                Width = 100,
                Height = 32
            };

            var btnApprove = new Button
            {
                Text = "Permitir (Sim)",
                DialogResult = DialogResult.Yes,
                Location = new Point(380, 235),
                Width = 100,
                Height = 32
            };

            btnDeny.Click += (_, _) =>
            {
                IsApproved = false;
                Close();
            };

            btnApprove.Click += (_, _) =>
            {
                IsApproved = true;
                Close();
            };

            AcceptButton = btnDeny; // Default é negar
            CancelButton = btnDeny;

            Controls.Add(titleLabel);
            Controls.Add(descTextBox);
            Controls.Add(_timerLabel);
            Controls.Add(btnDeny);
            Controls.Add(btnApprove);

            _countdownTimer = new Timer { Interval = 1000 };
            _countdownTimer.Tick += (_, _) =>
            {
                _secondsRemaining--;
                if (_secondsRemaining <= 0)
                {
                    _countdownTimer.Stop();
                    IsApproved = false;
                    Close();
                }
                else
                {
                    _timerLabel.Text = $"Tempo restante para resposta: {_secondsRemaining}s (negará automaticamente)";
                }
            };
            _countdownTimer.Start();

            FormClosing += (_, _) =>
            {
                _countdownTimer.Stop();
                _countdownTimer.Dispose();
            };
        }

        private static string GetCommandDescription(RemoteCommand command)
        {
            var requestedBy = string.IsNullOrWhiteSpace(command.RequestedBy) ? "Dispositivo Remoto" : command.RequestedBy;
            var details = command.Type switch
            {
                RemoteCommandType.TypeText =>
                    $"Tipo: Digitação de Texto (TypeText)\r\n" +
                    $"Processo Alvo: {command.Payload?.GetValueOrDefault("processName", "N/A")}\r\n" +
                    $"Texto a ser digitado:\r\n\"{command.Payload?.GetValueOrDefault("text", "")}\"",

                RemoteCommandType.ClickElement =>
                    $"Tipo: Clique em Elemento de UI (ClickElement)\r\n" +
                    $"Processo Alvo: {command.Payload?.GetValueOrDefault("processName", "N/A")}\r\n" +
                    $"Elemento de UI: \"{command.Payload?.GetValueOrDefault("elementName", "N/A")}\"",

                _ => $"Tipo: {command.Type}"
            };

            return $"Solicitado por: {requestedBy}\r\n" +
                   $"Identificador do Comando: {command.Id}\r\n\r\n" +
                   $"{details}\r\n\r\n" +
                   "Deseja permitir que esta ação seja executada agora no computador?";
        }
    }
}
