using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace Synapse.Tests.UI;

/// <summary>
/// Hospeda uma unica thread STA com Dispatcher e uma unica instancia de
/// <see cref="Application"/> para todas as capturas WPF.
///
/// Precisa ser unica: Application.Current e estatico por AppDomain e fica preso ao
/// Dispatcher da thread que o criou. Criar uma Application por teste geraria
/// InvalidOperationException de acesso cross-thread na segunda captura.
/// </summary>
public sealed class WpfAppFixture : IDisposable
{
    private readonly Thread _uiThread;
    private readonly ManualResetEventSlim _ready = new(false);

    public Dispatcher Dispatcher { get; private set; } = null!;

    public WpfAppFixture()
    {
        _uiThread = new Thread(() =>
        {
            Dispatcher = Dispatcher.CurrentDispatcher;

            if (Application.Current is null)
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (_, e) =>
                {
                    // Evita que exceções em tarefas fire-and-forget encerrem o loop do Dispatcher
                    e.Handled = true;
                };
            }

            _ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Synapse.WpfCaptureHost"
        };

        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        // 60s, nao 10: subir a Application do WPF carrega tema e as fontes pixel, e no runner do
        // CI (2 nucleos) isso disputa CPU com as outras colecoes que o xunit roda em paralelo. A
        // thread STA fica sem fatia de tempo e nao sinaliza dentro de 10s - derrubando de uma vez
        // as 19 suites de UI, todas com o mesmo erro de fixture. O trabalho e o mesmo; o que
        // estoura e o tempo de parede, entao o limite so precisa ser generoso o bastante para nao
        // transformar lentidao em falha. Na maquina de desenvolvimento sinaliza em milissegundos.
        if (!_ready.Wait(TimeSpan.FromSeconds(60)))
        {
            throw new InvalidOperationException(
                "A thread STA de captura WPF nao inicializou em 60s.");
        }
    }

    /// <summary>Executa uma acao na thread de UI e propaga a excecao original.</summary>
    public void Invoke(Action action) => Dispatcher.Invoke(action, DispatcherPriority.Normal);

    public T Invoke<T>(Func<T> func) => Dispatcher.Invoke(func, DispatcherPriority.Normal);

    public Task InvokeAsync(Action action)
    {
        Dispatcher.Invoke(action, DispatcherPriority.Normal);
        return Task.CompletedTask;
    }

    public async Task InvokeAsync(Func<Task> func)
    {
        var task = Dispatcher.Invoke(func, DispatcherPriority.Normal);
        await task;
    }

    public async Task<T> InvokeAsync<T>(Func<Task<T>> func)
    {
        var task = Dispatcher.Invoke(func, DispatcherPriority.Normal);
        return await task;
    }

    public void Dispose()
    {
        try
        {
            Dispatcher.InvokeShutdown();
            _uiThread.Join(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Encerramento best-effort: a thread e background e morre com o processo.
        }

        _ready.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class WpfCaptureCollection : ICollectionFixture<WpfAppFixture>
{
    public const string Name = "wpf-capture";
}
