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
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
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

        if (!_ready.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new InvalidOperationException("A thread STA de captura WPF nao inicializou.");
        }
    }

    /// <summary>Executa uma acao na thread de UI e propaga a excecao original.</summary>
    public void Invoke(Action action) => Dispatcher.Invoke(action, DispatcherPriority.Normal);

    public T Invoke<T>(Func<T> func) => Dispatcher.Invoke(func, DispatcherPriority.Normal);

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
