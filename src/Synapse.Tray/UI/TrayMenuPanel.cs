using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using System.Windows.Shapes;

namespace Synapse.Tray.UI;

/// <summary>Severidade do estado de sincronia, para a cor da faixa e do rotulo.</summary>
public enum TrayStatusKind
{
    Ok,
    Working,
    Warning,
    Error,
    Idle
}

/// <summary>
/// Cabeca do menu da bandeja: painel de status + os quatro ladrilhos das acoes diarias.
///
/// Vive numa classe propria porque duas coisas precisam renderizar exatamente o mesmo:
/// o ContextMenu real (que nao e capturavel, por viver num Popup com HWND proprio) e a
/// janela de prova dos testes de captura. Se cada um montasse o seu, a tela pontuada
/// deixaria de ser a tela entregue.
/// </summary>
public sealed class TrayMenuPanel : StackPanel
{
    private readonly Rectangle _stripe;
    private readonly TextBlock _stateText;
    private readonly TextBlock _detailText;
    private readonly List<Action> _tileActions = [];

    public event Action? QuickCaptureRequested;
    public event Action? ChatRequested;
    public event Action? FlashcardsRequested;
    public event Action? StatsRequested;

    public TrayMenuPanel()
    {
        PixelWindow.EnsureTheme();

        // ---- painel de status ----------------------------------------------
        _stripe = new Rectangle
        {
            Width = 6,
            Fill = Brush("SuccessBrush"),
            SnapsToDevicePixels = true
        };

        _stateText = new TextBlock
        {
            Text = "CONECTANDO...",
            FontFamily = (FontFamily)Res("FontDisplay"),
            FontSize = 12,
            Foreground = Brush("SuccessBrush"),
            Margin = new Thickness(0, 0, 0, 7)
        };

        _detailText = new TextBlock
        {
            Text = "-",
            FontFamily = (FontFamily)Res("FontBody"),
            FontSize = 13,
            Foreground = Brush("TextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var statusBody = new StackPanel { Margin = new Thickness(12, 10, 12, 11) };
        statusBody.Children.Add(_stateText);
        statusBody.Children.Add(_detailText);

        var statusRow = new DockPanel { Background = Brush("SurfaceAltBrush") };
        DockPanel.SetDock(_stripe, Dock.Left);
        statusRow.Children.Add(_stripe);
        statusRow.Children.Add(statusBody);

        Children.Add(statusRow);

        // ---- ladrilhos -------------------------------------------------------
        var tiles = new UniformGrid
        {
            Rows = 2,
            Columns = 2,
            Margin = new Thickness(0, 4, 0, 0)
        };

        // Rotulos em uma linha so: quebrar em duas produzia "ESTATIS / TICAS",
        // que parte a palavra no meio da silaba. Todos cabem na largura do ladrilho.
        tiles.Children.Add(Tile(1, "CAPTURA RAPIDA", TrayIcons.Bolt, "TextPrimaryBrush", primary: true,
            () => QuickCaptureRequested?.Invoke()));
        tiles.Children.Add(Tile(2, "CHAT COM COFRE", TrayIcons.Bubble, "AccentSecondaryBrush", primary: false,
            () => ChatRequested?.Invoke()));
        tiles.Children.Add(Tile(3, "FLASHCARDS", TrayIcons.Cards, "SuccessBrush", primary: false,
            () => FlashcardsRequested?.Invoke()));
        tiles.Children.Add(Tile(4, "ESTATISTICAS", TrayIcons.Bars, "WarningBrush", primary: false,
            () => StatsRequested?.Invoke()));

        Children.Add(tiles);
    }

    /// <summary>
    /// Embrulha o painel num MenuItem proprio, para uso dentro de um ContextMenu.
    ///
    /// Adicionar o painel direto em ContextMenu.Items nao funciona: o menu so aceita como
    /// container um item que ja seja MenuItem ou Separator, e envolve qualquer outro num
    /// MenuItem gerado — cujo template (PixelTheme) desenha o Header como texto. O painel
    /// virava uma faixa vazia. Com o MenuItem pronto, nao ha embrulho.
    /// </summary>
    public MenuItem AsMenuItem() => new()
    {
        Header = this,
        Style = (Style)Res("TrayMenuHead")
    };

    /// <summary>
    /// Dispara o ladrilho correspondente a uma tecla 1-4. Retorna true se tratou.
    ///
    /// Os ladrilhos sao Button, nao MenuItem, entao a navegacao por setas do ContextMenu
    /// nao passa por eles. Em vez de reimplementar essa navegacao, cada ladrilho ganha um
    /// acelerador numerico impresso no canto — mais rapido que seta, e visivel.
    /// </summary>
    public bool TryHandleAccelerator(Key key)
    {
        var index = key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            _ => -1
        };

        if (index < 0 || index >= _tileActions.Count)
        {
            return false;
        }

        _tileActions[index]();
        return true;
    }

    /// <summary>Atualiza o bloco de status. Chamado a cada poll de IPC.</summary>
    public void SetStatus(TrayStatusKind kind, string state, string detail)
    {
        var key = kind switch
        {
            TrayStatusKind.Ok => "SuccessBrush",
            TrayStatusKind.Working => "AccentPrimaryBrush",
            TrayStatusKind.Warning => "WarningBrush",
            TrayStatusKind.Error => "ErrorBrush",
            _ => "TextSecondaryBrush"
        };

        var brush = Brush(key);
        _stripe.Fill = brush;
        _stateText.Foreground = brush;
        _stateText.Text = state.ToUpperInvariant();
        _detailText.Text = detail;
    }

    private Button Tile(int number, string label, string iconPattern, string iconBrushKey, bool primary, Action onClick)
    {
        var icon = new PixelIcon
        {
            Pattern = iconPattern,
            Scale = 2,
            Fill = Brush(iconBrushKey),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 9)
        };

        var caption = new TextBlock
        {
            Text = label,
            FontFamily = (FontFamily)Res("FontBody"),
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            Foreground = Brush("TextPrimaryBrush")
        };

        var stack = new StackPanel();
        stack.Children.Add(icon);
        stack.Children.Add(caption);

        // Acelerador impresso no canto: e o que torna o ladrilho alcancavel sem mouse.
        var badge = new TextBlock
        {
            Text = number.ToString(),
            FontFamily = (FontFamily)Res("FontBody"),
            FontSize = 12,
            Foreground = Brush(primary ? "AccentPrimaryEdgeLightBrush" : "TextDisabledBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 0, 0, 0)
        };

        var content = new Grid();
        content.Children.Add(stack);
        content.Children.Add(badge);

        var button = new Button
        {
            Content = content,
            Style = (Style)Res(primary ? "TrayTilePrimary" : "TrayTile"),
            Focusable = true
        };

        button.Click += (_, _) => onClick();
        _tileActions.Add(onClick);
        return button;
    }

    private static object Res(string key) => Application.Current.FindResource(key);

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}
