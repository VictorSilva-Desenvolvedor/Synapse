using System.Windows;
using Synapse.Tray.UI;

namespace Synapse.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Synapse");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, "tray_startup.log");
        File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Main starting...\n");

        SynapseTrayApp? tray = null;

        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Domain UnhandledException: {e.ExceptionObject}\n");
            };

            using var mutex = new Mutex(true, "Local\\Synapse.Tray.Mutex.App", out var createdNew);
            File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Mutex createdNew={createdNew}\n");

            if (!createdNew && !mutex.WaitOne(TimeSpan.FromMilliseconds(500), false))
            {
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Mutex wait failed (already running)\n");
                return;
            }

            // Aplicacao WPF sem janela principal: a bandeja e que decide o ciclo de vida,
            // entao o shutdown precisa ser explicito - caso contrario o app encerraria
            // assim que a ultima janela fosse fechada.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            app.DispatcherUnhandledException += (_, e) =>
            {
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] DispatcherUnhandledException: {e.Exception}\n");
                e.Handled = true;
            };

            PixelWindow.EnsureTheme();
            File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Tema pixel art carregado\n");

            tray = new SynapseTrayApp();
            File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Bandeja iniciada, entrando no loop...\n");

            app.Run();
            File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Application.Run finished cleanly\n");
        }
        catch (Exception ex)
        {
            File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] FATAL EXCEPTION: {ex}\n");
            MessageBox.Show($"Erro critico ao iniciar Synapse.Tray:\n{ex}", "Synapse - Erro Critico",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            tray?.Dispose();
        }
    }
}
