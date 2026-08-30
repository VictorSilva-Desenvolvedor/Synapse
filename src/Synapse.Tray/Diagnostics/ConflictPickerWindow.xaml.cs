using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Synapse.Tray.UI;

namespace Synapse.Tray.Diagnostics;

/// <summary>
/// Lista de conflitos preservados, aberta a partir do aviso do Diagnostico.
/// Cada conflito e um cartao com a acao dentro dele.
/// </summary>
public partial class ConflictPickerWindow : PixelWindow
{
    private readonly string _vaultPath;

    /// <summary>True se algum conflito foi resolvido, para o chamador recarregar a lista.</summary>
    public bool ResolvedAny { get; private set; }

    public ConflictPickerWindow(string vaultPath, IReadOnlyList<ConflictRow> conflicts)
    {
        _vaultPath = vaultPath;

        InitializeComponent();

        ConflictsList.ItemsSource = conflicts;
        Subtitle = conflicts.Count == 1
            ? "1 conflito preservado. Nenhum dado foi perdido."
            : $"{conflicts.Count} conflitos preservados. Nenhum dado foi perdido.";
    }

    private void OnResolve(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ConflictRow row } || !File.Exists(row.FullPath))
        {
            return;
        }

        var viewer = new ThreeWayDiffWindow(_vaultPath, row.FullPath) { Owner = this };
        if (viewer.ShowDialog() == true)
        {
            ResolvedAny = true;
            Close();
        }
    }

    /// <summary>
    /// Abre o arquivo do cartao clicado. O alvo vem do DataContext do proprio botao —
    /// antes este metodo agia sobre Items[0] a partir de um botao no rodape, o que
    /// operava num item que o usuario nao tinha como identificar.
    /// </summary>
    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConflictRow row } && File.Exists(row.FullPath))
        {
            Process.Start(new ProcessStartInfo { FileName = row.FullPath, UseShellExecute = true });
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
