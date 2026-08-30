using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Synapse.Tray.UI;
using Xunit;

namespace Synapse.Tests.UI;

/// <summary>
/// O painel da bandeja e montado em codigo e depois entregue ao ContextMenu real.
/// A janela de prova das capturas o hospeda dentro de um StackPanel, onde qualquer
/// UIElement renderiza sozinho — o ContextMenu NAO se comporta assim.
/// </summary>
[Collection(WpfCaptureCollection.Name)]
public sealed class TrayContextMenuTests
{
    private readonly WpfAppFixture _fixture;

    public TrayContextMenuTests(WpfAppFixture fixture) => _fixture = fixture;

    [Fact]
    public void PainelDaBandeja_RenderizaDentroDoContextMenuReal()
    {
        var achou = _fixture.Invoke(() =>
        {
            var panel = new TrayMenuPanel();
            panel.SetStatus(TrayStatusKind.Ok, "Sincronizado", "ultimo sync 14:32");

            var host = new Window { Width = 200, Height = 200, ShowActivated = false };
            host.Show();

            var menu = new ContextMenu
            {
                PlacementTarget = host,
                Items = { panel.AsMenuItem(), new Separator(), new MenuItem { Header = "Sair da Bandeja" } }
            };

            menu.IsOpen = true;
            menu.UpdateLayout();

            var root = (Visual?)PresentationSource.FromVisual(menu)?.RootVisual ?? menu;
            var encontrado = Find<PixelIcon>(root) is not null;

            menu.IsOpen = false;
            host.Close();
            return encontrado;
        });

        Assert.True(achou, "Nenhum PixelIcon do painel chegou a arvore visual do ContextMenu.");
    }

    private static T? Find<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T hit)
        {
            return hit;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var found = Find<T>(VisualTreeHelper.GetChild(node, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
