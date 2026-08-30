using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Synapse.Sync.Config;
using Synapse.Tray.UI;

namespace Synapse.Tray.RemoteApps;

public partial class AllowedAppsWindow : PixelWindow
{
    private readonly SynapseConfigManager _configManager;
    private SynapseConfig _config = new();
    private List<DiscoveredApp> _allDiscoveredApps = [];

    public AllowedAppsWindow(SynapseConfigManager? configManager = null)
    {
        InitializeComponent();
        _configManager = configManager ?? new SynapseConfigManager();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadConfigAsync();
        await ScanShortcutsAsync();
    }

    private async Task LoadConfigAsync()
    {
        _config = await _configManager.LoadAsync();
        _config.RemoteAllowedApps ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        RenderApprovedApps();
    }

    private async Task ScanShortcutsAsync()
    {
        DiscoveredCountText.Text = "Varrendo atalhos do Menu Iniciar...";
        _allDiscoveredApps = await Task.Run(() => StartMenuShortcutScanner.Scan());
        RenderDiscoveredApps();
    }

    private void RenderApprovedApps()
    {
        ApprovedListPanel.Children.Clear();
        var apps = _config.RemoteAllowedApps.ToList();

        ApprovedCountText.Text = $"CONFIGURADOS: {apps.Count}";

        if (apps.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "Nenhum aplicativo configurado.\nAdicione abaixo ou aprove atalhos na aba 'Descobrir Atalhos'.",
                Style = (Style)FindResource("PixelCaption"),
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 30, 0, 30)
            };
            ApprovedListPanel.Children.Add(emptyText);
            return;
        }

        foreach (var (key, path) in apps.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
        {
            var card = CreateApprovedAppCard(key, path);
            ApprovedListPanel.Children.Add(card);
        }
    }

    private Border CreateApprovedAppCard(string key, string path)
    {
        var border = new Border
        {
            Style = (Style)FindResource("AppCard")
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var infoPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var keyBadge = new Border
        {
            Style = (Style)FindResource("BadgeKey"),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock
            {
                Text = key,
                Foreground = (Brush)FindResource("AccentPrimaryBrush"),
                FontFamily = (FontFamily)FindResource("FontBody"),
                FontSize = (double)FindResource("FontSizeSmall")
            }
        };
        keyRow.Children.Add(keyBadge);
        infoPanel.Children.Add(keyRow);

        var pathBlock = new TextBlock
        {
            Text = path,
            FontFamily = (FontFamily)FindResource("FontMono"),
            FontSize = (double)FindResource("FontSizeMono"),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = path
        };
        infoPanel.Children.Add(pathBlock);

        Grid.SetColumn(infoPanel, 0);
        grid.Children.Add(infoPanel);

        var removeButton = new Button
        {
            Content = "REMOVER",
            Style = (Style)FindResource("PixelButtonDanger"),
            Height = 32,
            Padding = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        removeButton.Click += async (_, _) => await RemoveAppAsync(key);

        Grid.SetColumn(removeButton, 1);
        grid.Children.Add(removeButton);

        border.Child = grid;
        return border;
    }

    private void RenderDiscoveredApps()
    {
        DiscoveredListPanel.Children.Clear();

        var query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allDiscoveredApps
            : _allDiscoveredApps.Where(a =>
                a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.SuggestedKey.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.ShortcutPath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        DiscoveredCountText.Text = $"{filtered.Count} de {_allDiscoveredApps.Count} atalhos encontrados";

        if (filtered.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(query)
                    ? "Nenhum atalho encontrado no Menu Iniciar."
                    : "Nenhum atalho corresponde ao filtro informado.",
                Style = (Style)FindResource("PixelCaption"),
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 30, 0, 30)
            };
            DiscoveredListPanel.Children.Add(emptyText);
            return;
        }

        foreach (var app in filtered)
        {
            var card = CreateDiscoveredAppCard(app);
            DiscoveredListPanel.Children.Add(card);
        }
    }

    private Border CreateDiscoveredAppCard(DiscoveredApp app)
    {
        var border = new Border
        {
            Style = (Style)FindResource("AppCard")
        };

        var isAlreadyApproved = _config.RemoteAllowedApps.ContainsKey(app.SuggestedKey);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var infoPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var nameBlock = new TextBlock
        {
            Text = app.Name,
            Style = (Style)FindResource("PixelHeadline"),
            FontSize = (double)FindResource("FontSizeSmall"),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        headerRow.Children.Add(nameBlock);

        var keyBadge = new Border
        {
            Style = (Style)FindResource("BadgeKey"),
            Child = new TextBlock
            {
                Text = $"chave: {app.SuggestedKey}",
                Foreground = (Brush)FindResource("AccentSecondaryBrush"),
                FontFamily = (FontFamily)FindResource("FontBody"),
                FontSize = (double)FindResource("FontSizeSmall")
            }
        };
        headerRow.Children.Add(keyBadge);
        infoPanel.Children.Add(headerRow);

        var pathBlock = new TextBlock
        {
            Text = app.ShortcutPath,
            FontFamily = (FontFamily)FindResource("FontMono"),
            FontSize = (double)FindResource("FontSizeMono"),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = app.ShortcutPath
        };
        infoPanel.Children.Add(pathBlock);

        Grid.SetColumn(infoPanel, 0);
        grid.Children.Add(infoPanel);

        var approveButton = new Button
        {
            Content = isAlreadyApproved ? "ATUALIZAR" : "APROVAR",
            Style = isAlreadyApproved ? (Style)FindResource("PixelButtonSecondary") : (Style)FindResource("PixelButtonPrimary"),
            Height = 32,
            Padding = new Thickness(14, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        approveButton.Click += async (_, _) => await ApproveAppAsync(app.SuggestedKey, app.ShortcutPath);

        Grid.SetColumn(approveButton, 1);
        grid.Children.Add(approveButton);

        border.Child = grid;
        return border;
    }

    private async Task ApproveAppAsync(string key, string path)
    {
        if (_config.RemoteAllowedApps.TryGetValue(key, out var existingPath))
        {
            if (string.Equals(existingPath, path, StringComparison.OrdinalIgnoreCase))
            {
                PixelMessageBox.Show($"O aplicativo '{key}' já está aprovado com este caminho.", "AVISO", PixelMessageKind.Info, this);
                return;
            }

            var confirmed = PixelMessageBox.Confirm(
                $"A chave '{key}' já está associada a:\n{existingPath}\n\nDeseja sobrescrever com o novo caminho?",
                "CONFIRMAR SOBRESCRITA",
                PixelMessageKind.Warning,
                this);

            if (!confirmed)
            {
                return;
            }
        }

        _config.RemoteAllowedApps[key] = path;
        await _configManager.SaveAsync(_config);

        RenderApprovedApps();
        RenderDiscoveredApps();

        PixelMessageBox.Show($"Aplicativo '{key}' aprovado com sucesso!", "SUCESSO", PixelMessageKind.Success, this);
    }

    private async Task RemoveAppAsync(string key)
    {
        if (_config.RemoteAllowedApps.Remove(key))
        {
            await _configManager.SaveAsync(_config);
            RenderApprovedApps();
            RenderDiscoveredApps();
        }
    }

    private async void OnAddManualApp(object sender, RoutedEventArgs e)
    {
        var key = ManualKeyBox.Text.Trim();
        var path = ManualPathBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(key))
        {
            PixelMessageBox.Show("Informe uma chave simbólica (ex: notepad, calculadora).", "CAMPO OBRIGATÓRIO", PixelMessageKind.Warning, this);
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            PixelMessageBox.Show("Informe o caminho do executável ou comando.", "CAMPO OBRIGATÓRIO", PixelMessageKind.Warning, this);
            return;
        }

        await ApproveAppAsync(key, path);

        ManualKeyBox.Text = string.Empty;
        ManualPathBox.Text = string.Empty;
    }

    private void OnBrowseExecutable(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar Aplicativo ou Atalho",
            Filter = "Aplicativos e Atalhos (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Todos os Arquivos (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            ManualPathBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(ManualKeyBox.Text))
            {
                var fileName = Path.GetFileNameWithoutExtension(dialog.FileName);
                ManualKeyBox.Text = StartMenuShortcutScanner.GenerateSuggestedKey(fileName);
            }
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        RenderDiscoveredApps();
    }

    private async void OnRefreshShortcuts(object sender, RoutedEventArgs e)
    {
        await ScanShortcutsAsync();
    }
}
