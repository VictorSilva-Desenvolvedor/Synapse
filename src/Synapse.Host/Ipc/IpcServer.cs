using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Synapse.Host.Ipc;

/// <summary>
/// Servidor de Named Pipe para IPC local entre o serviço em background e o processo de bandeja (ADR-010, API seção 3).
/// </summary>
public sealed class IpcServer : IAsyncDisposable
{
    public const string DefaultPipeName = "synapse-ipc";
    public string PipeName { get; }

    private readonly Func<IpcStatusPayload> _getStatusHandler;
    private readonly Func<Task<IpcStatusPayload>> _pauseHandler;
    private readonly Func<Task<IpcStatusPayload>> _resumeHandler;
    private readonly Func<Task<IpcStatusPayload>> _reconnectHandler;
    private readonly Func<string> _getLogPathHandler;
    private readonly ILogger<IpcServer>? _logger;
    private readonly List<StreamWriter> _activeClients = new();
    private readonly object _clientsLock = new();

    public IpcServer(
        Func<IpcStatusPayload> getStatusHandler,
        Func<Task<IpcStatusPayload>> pauseHandler,
        Func<Task<IpcStatusPayload>> resumeHandler,
        Func<Task<IpcStatusPayload>> reconnectHandler,
        Func<string> getLogPathHandler,
        string? pipeName = null,
        ILogger<IpcServer>? logger = null)
    {
        PipeName = pipeName ?? DefaultPipeName;
        _getStatusHandler = getStatusHandler ?? throw new ArgumentNullException(nameof(getStatusHandler));
        _pauseHandler = pauseHandler ?? throw new ArgumentNullException(nameof(pauseHandler));
        _resumeHandler = resumeHandler ?? throw new ArgumentNullException(nameof(resumeHandler));
        _reconnectHandler = reconnectHandler ?? throw new ArgumentNullException(nameof(reconnectHandler));
        _getLogPathHandler = getLogPathHandler ?? throw new ArgumentNullException(nameof(getLogPathHandler));
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger?.LogInformation("Servidor IPC Named Pipe iniciando em \\\\.\\pipe\\{PipeName}", PipeName);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                using var reg = ct.Register(state => ((IDisposable)state!).Dispose(), pipeServer);
                await pipeServer.WaitForConnectionAsync(ct);
                _ = HandleClientConnectionAsync(pipeServer, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erro no loop de escuta do Named Pipe IPC.");
                await Task.Delay(1000, ct);
            }
        }
    }

    public async Task BroadcastEventAsync(string tipo, object payload, CancellationToken ct = default)
    {
        var envelope = new IpcEnvelope
        {
            Versao = 1,
            Tipo = tipo,
            Payload = payload
        };

        var json = JsonSerializer.Serialize(envelope);

        List<StreamWriter> clients;
        lock (_clientsLock)
        {
            clients = _activeClients.ToList();
        }

        foreach (var writer in clients)
        {
            try
            {
                await writer.WriteLineAsync(json.AsMemory(), ct);
                await writer.FlushAsync(ct);
            }
            catch
            {
                lock (_clientsLock)
                {
                    _activeClients.Remove(writer);
                }
            }
        }
    }

    private async Task HandleClientConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        _logger?.LogInformation("Novo cliente conectado ao Named Pipe IPC.");
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        lock (_clientsLock)
        {
            _activeClients.Add(writer);
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                var envelope = JsonSerializer.Deserialize<IpcEnvelope>(line);
                if (envelope == null) continue;

                var responseEnvelope = await ProcessCommandAsync(envelope);
                if (responseEnvelope != null)
                {
                    var responseJson = JsonSerializer.Serialize(responseEnvelope);
                    await writer.WriteLineAsync(responseJson);
                    await writer.FlushAsync();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogDebug(ex, "Conexão de cliente IPC encerrada.");
        }
        finally
        {
            lock (_clientsLock)
            {
                _activeClients.Remove(writer);
            }
            await pipe.DisposeAsync();
        }
    }

    internal async Task<IpcEnvelope> ProcessCommandAsync(IpcEnvelope envelope)
    {
        switch (envelope.Tipo)
        {
            case "GetStatus":
                return new IpcEnvelope
                {
                    Tipo = "StatusChanged",
                    Payload = _getStatusHandler()
                };

            case "Pause":
                var pausedStatus = await _pauseHandler();
                return new IpcEnvelope
                {
                    Tipo = "StatusChanged",
                    Payload = pausedStatus
                };

            case "Resume":
                var resumedStatus = await _resumeHandler();
                return new IpcEnvelope
                {
                    Tipo = "StatusChanged",
                    Payload = resumedStatus
                };

            case "Reconnect":
                var reconnectedStatus = await _reconnectHandler();
                return new IpcEnvelope
                {
                    Tipo = "StatusChanged",
                    Payload = reconnectedStatus
                };

            case "GetLogPath":
                return new IpcEnvelope
                {
                    Tipo = "LogPath",
                    Payload = new IpcLogPathPayload { Caminho = _getLogPathHandler() }
                };

            default:
                _logger?.LogWarning("Comando IPC desconhecido recebido: {Tipo}", envelope.Tipo);
                return new IpcEnvelope
                {
                    Tipo = "Error",
                    Payload = new { mensagem = $"Comando desconhecido: {envelope.Tipo}" }
                };
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_clientsLock)
        {
            _activeClients.Clear();
        }
        return ValueTask.CompletedTask;
    }
}
