namespace Synapse.Tray;

internal static class Program
{
    private const string MutexName = "Global\\Synapse.Tray.Mutex.SingleInstance";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Já existe uma instância da bandeja em execução
            return;
        }

        Application.Run(new TrayApplicationContext());
    }
}
