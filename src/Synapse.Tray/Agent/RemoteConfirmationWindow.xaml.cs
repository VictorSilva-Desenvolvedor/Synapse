using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Synapse.Agent;
using Synapse.Agent.Models;
using Synapse.Tray.UI;

namespace Synapse.Tray.Agent;

/// <summary>
/// Portao de autorizacao de comando remoto, com nivel de risco.
///
/// SEGURANCA - o padrao e NEGAR em todos os caminhos de saida: estouro do contador,
/// Esc, Enter (o botao Negar e IsDefault) e fechar a janela. So um clique explicito em
/// PERMITIR aprova. Preservar isso ao editar.
///
/// A cor dos botoes foi invertida em relacao a versao anterior. Antes PERMITIR era o
/// unico botao preenchido, em vermelho — mas vermelho significa "pare" por convencao, e
/// sob um cronometro de 30s a acao arriscada estava vestida com a cor que o reflexo le
/// como segura. Agora NEGAR carrega o peso visual e PERMITIR e um contorno discreto.
/// </summary>
public partial class RemoteConfirmationWindow : PixelWindow
{
    private readonly DispatcherTimer _countdown;
    private readonly int _totalSeconds;
    private int _secondsRemaining;

    public bool IsApproved { get; private set; }

    public RemoteConfirmationWindow(RemoteCommand command, TimeSpan timeout, CancellationToken ct = default)
    {
        _totalSeconds = (int)Math.Max(1, timeout.TotalSeconds);
        _secondsRemaining = _totalSeconds;

        InitializeComponent();

        ApplyCommand(command);
        UpdateCountdown();

        _countdown = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _countdown.Tick += (_, _) =>
        {
            _secondsRemaining--;
            if (_secondsRemaining <= 0)
            {
                _countdown.Stop();
                IsApproved = false;
                Close();
                return;
            }

            UpdateCountdown();
        };

        Loaded += (_, _) =>
        {
            _countdown.Start();
            DenyButton.Focus();
            Activate();
        };

        Closing += (_, _) => _countdown.Stop();

        if (ct.CanBeCanceled)
        {
            ct.Register(() => Dispatcher.BeginInvoke(() =>
            {
                IsApproved = false;
                Close();
            }));
        }
    }

    /// <summary>Preenche a ficha a partir da descricao classificada do comando.</summary>
    private void ApplyCommand(RemoteCommand command)
    {
        var d = RemoteCommandDescriber.Describe(command);

        var riskBrush = (Brush)FindResource(d.Risk switch
        {
            RemoteCommandRisk.Low => "SuccessBrush",
            RemoteCommandRisk.Medium => "WarningBrush",
            _ => "ErrorBrush"
        });

        RiskStripe.Fill = riskBrush;
        RiskLabel.Foreground = riskBrush;
        RiskLabel.Text = d.RiskLabel;
        RiskReason.Text = d.RiskReason;
        AccentBrush = riskBrush;

        Subtitle = $"{command.RequestedBy} - {d.Action.ToLowerInvariant()}";

        RequesterText.Text = string.IsNullOrWhiteSpace(command.RequestedBy)
            ? "Dispositivo remoto"
            : command.RequestedBy;

        AgeText.Text = DescribeAge(command.CreatedAt);
        ActionText.Text = d.Action;
        ActionText.Foreground = riskBrush;

        if (d.Target is null)
        {
            TargetKey.Visibility = Visibility.Collapsed;
            TargetText.Visibility = Visibility.Collapsed;
        }
        else
        {
            TargetText.Text = d.Target;
        }

        if (d.Payload is null)
        {
            PayloadBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            PayloadText.Text = d.Payload;
        }
    }

    /// <summary>
    /// A idade importa: um comando parado na fila ha uma hora merece mais desconfianca
    /// que um recem-criado, e o GUID que ocupava esse espaco nao ajudava a decidir nada.
    /// </summary>
    private static string DescribeAge(DateTimeOffset createdAt)
    {
        var age = DateTimeOffset.UtcNow - createdAt;

        if (age < TimeSpan.Zero)
        {
            return "agora";
        }

        if (age.TotalSeconds < 10)
        {
            return "agora mesmo";
        }

        if (age.TotalSeconds < 60)
        {
            return $"ha {(int)age.TotalSeconds} segundos";
        }

        if (age.TotalMinutes < 60)
        {
            var minutos = (int)age.TotalMinutes;
            return minutos == 1 ? "ha 1 minuto" : $"ha {minutos} minutos";
        }

        var horas = (int)age.TotalHours;
        return horas == 1
            ? "ha 1 hora - pedido antigo"
            : $"ha {horas} horas - pedido antigo";
    }

    private void UpdateCountdown()
    {
        CountdownText.Text = $"{_secondsRemaining}S ATE NEGAR AUTOMATICAMENTE";

        // Duas colunas em estrela: a barra esvazia sem depender de ActualWidth.
        TimeLeftColumn.Width = new GridLength(Math.Max(_secondsRemaining, 0), GridUnitType.Star);
        TimeSpentColumn.Width = new GridLength(Math.Max(_totalSeconds - _secondsRemaining, 0), GridUnitType.Star);
    }

    private void OnApprove(object sender, RoutedEventArgs e)
    {
        IsApproved = true;
        Close();
    }

    private void OnDeny(object sender, RoutedEventArgs e)
    {
        IsApproved = false;
        Close();
    }
}
