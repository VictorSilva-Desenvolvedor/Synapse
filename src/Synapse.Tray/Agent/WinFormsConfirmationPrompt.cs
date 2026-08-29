using Synapse.Agent;
using Synapse.Agent.Models;
using Synapse.Tray.UI;
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

    internal sealed class RemoteConfirmationForm : Form
    {
        private readonly Timer _countdownTimer;
        private int _secondsRemaining;
        private readonly Label _timerLabel;

        public bool IsApproved { get; private set; }

        public RemoteConfirmationForm(RemoteCommand command, TimeSpan timeout, CancellationToken ct)
        {
            _secondsRemaining = (int)Math.Max(1, timeout.TotalSeconds);

            Text = "Synapse Remote — Solicitação de Ação Sensível [Pixel Edition]";
            Width = 560;
            Height = 390;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            SynapseTheme.ApplyFormChrome(this);

            var pnlHeader = SynapseTheme.CreateHeaderBar(
                "► AUTORIZAÇÃO DE CONTROLE REMOTO",
                "Uma ação sensível foi solicitada por um dispositivo remoto",
                65);
            Controls.Add(pnlHeader);

            var description = GetCommandDescription(command);
            var descTextBox = new TextBox
            {
                Text = description,
                Location = new Point(20, 80),
                Width = 505,
                Height = 180,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Font = SynapseTheme.FontMono(8.5f)
            };
            SynapseTheme.StyleInput(descTextBox);

            _timerLabel = SynapseTheme.CreateStatusBadge($"⏱ {_secondsRemaining}s — NEGARÁ AUTOMATICAMENTE", SynapseTheme.Warning);
            _timerLabel.Location = new Point(20, 275);

            var btnDeny = new SynapseButton
            {
                Text = "✖ Negar (Não)",
                DialogResult = DialogResult.No,
                Location = new Point(275, 305),
                Width = 120,
                Height = 36,
                Variant = SynapseButtonVariant.Secondary
            };

            var btnApprove = new SynapseButton
            {
                Text = "✔ Permitir (Sim)",
                DialogResult = DialogResult.Yes,
                Location = new Point(405, 305),
                Width = 120,
                Height = 36,
                Variant = SynapseButtonVariant.Danger
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

            AcceptButton = btnDeny; // Default de segurança: NEGA POR PADRÃO
            CancelButton = btnDeny;

            Controls.Add(descTextBox);
            Controls.Add(_timerLabel);
            Controls.Add(btnDeny);
            Controls.Add(btnApprove);
            pnlHeader.BringToFront();

            Shown += (_, _) =>
            {
                descTextBox.SelectionStart = 0;
                descTextBox.SelectionLength = 0;
                btnDeny.Focus();
            };

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
                    _timerLabel.Text = $"⏱ {_secondsRemaining}s — NEGARÁ AUTOMATICAMENTE";
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
