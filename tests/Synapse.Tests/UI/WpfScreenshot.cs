using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Synapse.Tests.UI;

/// <summary>
/// Captura de telas WPF em 1:1 para o ciclo do agente pixel-art-frontend.
///
/// LEI ZERO — a captura e a terceira das quatro etapas onde um pixel pode ser
/// interpolado. Por isso o RenderTargetBitmap e sempre construido em 96 DPI:
/// qualquer outro valor faz o WPF reamostrar a arvore visual e borra a arte.
/// Em 96 DPI, 1 DIP = 1 pixel do PNG, independentemente do DPI do monitor.
/// </summary>
public static class WpfScreenshot
{
    public static readonly string OutputDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "screenshots"));

    /// <summary>
    /// Mostra a janela na thread de UI, espera o layout e o render estabilizarem,
    /// grava o PNG e fecha a janela.
    /// </summary>
    /// <param name="fixture">Host STA compartilhado.</param>
    /// <param name="factory">Cria a janela. Roda na thread de UI.</param>
    /// <param name="fileName">Nome do PNG, ex.: "03_QuickCapture.png".</param>
    /// <param name="setup">Popula a janela com dados de exemplo antes da captura.</param>
    /// <returns>Caminho absoluto do PNG gravado.</returns>
    public static string Capture(
        WpfAppFixture fixture,
        Func<Window> factory,
        string fileName,
        Action<Window>? setup = null)
    {
        Directory.CreateDirectory(OutputDir);
        var targetPath = Path.Combine(OutputDir, fileName);

        fixture.Invoke(() =>
        {
            Window? window = null;
            try
            {
                window = factory();

                // Fora da tela: a captura nao depende de estar visivel, e assim a
                // suite nao rouba o foco de quem estiver usando a maquina.
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -10000;
                window.Top = -10000;
                window.ShowInTaskbar = false;

                window.Show();

                // Drena a fila do Dispatcher ate Loaded: sem isso o RenderTargetBitmap
                // captura uma janela em branco, porque o layout ainda nao rodou.
                Flush(window.Dispatcher);

                // O setup roda DEPOIS do Loaded, de proposito. Varias telas carregam o
                // proprio estado em Loaded (config, indice RAG, metricas); se os dados de
                // exemplo fossem aplicados antes, o handler da janela os sobrescreveria e
                // a captura mostraria o estado de carregamento em vez da tela populada.
                setup?.Invoke(window);

                window.UpdateLayout();
                Flush(window.Dispatcher);

                var width = (int)Math.Ceiling(window.ActualWidth);
                var height = (int)Math.Ceiling(window.ActualHeight);

                if (width <= 0 || height <= 0)
                {
                    throw new InvalidOperationException(
                        $"Janela '{fileName}' mediu {width}x{height}. O layout nao rodou.");
                }

                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var stream = File.Create(targetPath);
                encoder.Save(stream);
            }
            finally
            {
                window?.Close();
            }
        });

        return targetPath;
    }

    /// <summary>
    /// Processa a fila do Dispatcher ate a prioridade Loaded, garantindo que measure,
    /// arrange e o primeiro render tenham acontecido.
    /// </summary>
    private static void Flush(Dispatcher dispatcher)
    {
        dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }
}
