namespace Synapse.Tray;

internal static class Program
{
    private const string MutexName = "Local\\Synapse.Tray.Mutex.SingleInstance";

    [STAThread]
    private static void Main()
    {
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Synapse");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, "tray_startup.log");
        File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Main starting...\n");

        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] ThreadException: {e.Exception}\n");
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Domain UnhandledException: {e.ExceptionObject}\n");
            };

            ApplicationConfiguration.Initialize();
            File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] ApplicationConfiguration initialized\n");

            using var mutex = new Mutex(true, "Local\\Synapse.Tray.Mutex.App", out var createdNew);
            File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Mutex createdNew={createdNew}\n");
            if (!createdNew)
            {
                if (!mutex.WaitOne(TimeSpan.FromMilliseconds(500), false))
                {
                    File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Mutex wait failed (already running)\n");
                    return;
                }
            }

            File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Running TrayApplicationContext...\n");
            Application.Run(new TrayApplicationContext());
            File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] Application.Run finished cleanly\n");
        }
        catch (Exception ex)
        {
            File.AppendAllText(logFile, $"[{DateTime.UtcNow:o}] FATAL EXCEPTION: {ex}\n");
            MessageBox.Show($"Erro crítico ao iniciar Synapse.Tray:\n{ex}", "Synapse - Erro Crítico", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
