using System.Windows;
using System.Windows.Media;

namespace Synapse.Tray.UI;

/// <summary>Severidade da mensagem. Define a cor da faixa de acento da barra de topo.</summary>
public enum PixelMessageKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// Substitui o MessageBox do sistema, que renderiza com cantos arredondados e sombra
/// difusa do Win11 e reprovaria qualquer tela na rubrica pixel art.
/// </summary>
public partial class PixelMessageBox : PixelWindow
{
    private bool _confirmed;

    public PixelMessageBox()
    {
        InitializeComponent();
    }

    /// <summary>Exibe uma mensagem simples com um unico botao OK.</summary>
    public static void Show(
        string message,
        string title = "SYNAPSE",
        PixelMessageKind kind = PixelMessageKind.Info,
        Window? owner = null)
    {
        Build(message, title, kind, owner, showCancel: false).ShowDialog();
    }

    /// <summary>Exibe uma confirmacao. Retorna true se o usuario confirmou.</summary>
    public static bool Confirm(
        string message,
        string title = "CONFIRMAR",
        PixelMessageKind kind = PixelMessageKind.Warning,
        Window? owner = null)
    {
        var box = Build(message, title, kind, owner, showCancel: true);
        box.ShowDialog();
        return box._confirmed;
    }

    private static PixelMessageBox Build(
        string message,
        string title,
        PixelMessageKind kind,
        Window? owner,
        bool showCancel)
    {
        var box = new PixelMessageBox
        {
            Title = title.ToUpperInvariant(),
            AccentBrush = AccentFor(kind)
        };

        box.MessageText.Text = message;
        box.CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;

        if (owner is not null && !ReferenceEquals(owner, box))
        {
            box.Owner = owner;
            box.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        return box;
    }

    private static Brush AccentFor(PixelMessageKind kind)
    {
        var key = kind switch
        {
            PixelMessageKind.Success => "SuccessBrush",
            PixelMessageKind.Warning => "WarningBrush",
            PixelMessageKind.Error => "ErrorBrush",
            _ => "AccentPrimaryBrush"
        };

        return (Brush)Application.Current.FindResource(key);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _confirmed = false;
        Close();
    }
}
