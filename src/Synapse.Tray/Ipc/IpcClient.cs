using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Synapse.Tray.Ipc;

/// <summary>
/// Cliente IPC assíncrono para comunicação via Named Pipe com o serviço Synapse.Host (ADR-010).
/// Suporta envio de comandos síncronos/assíncronos e reconexão automática com o serviço.
/// </summary>
public sealed class IpcClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly string _serverName;
    private NamedPipeClientStream? _pipeClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool IsConnected => _pipeClient?.IsConnected ?? (_reader != null);

    public event EventHandler<bool>? ConnectionChanged;

    public IpcClient(string? pipeName = null, string serverName = ".")
    {
        _pipeName = pipeName ?? "synapse-ipc";
        _serverName = serverName;
    }

    internal IpcClient(Stream stream)
    {
        _pipeName = string.Empty;
        _serverName = string.Empty;
        _reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        _writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
    }

    public async Task<bool> ConnectAsync(int timeoutMs = 2000, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_pipeName))
        {
            return _reader != null;
        }

        await DisconnectAsync();

        try
        {
            _pipeClient = new NamedPipeClientStream(_serverName, _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipeClient.ConnectAsync(timeoutMs, ct);

            _reader = new StreamReader(_pipeClient, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            _writer = new StreamWriter(_pipeClient, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

            ConnectionChanged?.Invoke(this, true);
            return true;
        }
        catch
        {
            await DisconnectAsync();
            ConnectionChanged?.Invoke(this, false);
            return false;
        }
    }

    public async Task<IpcStatusPayload?> GetStatusAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync("GetStatus", null, ct);
        return DeserializePayload<IpcStatusPayload>(response?.Payload);
    }

    public async Task<IpcStatusPayload?> PauseAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync("Pause", null, ct);
        return DeserializePayload<IpcStatusPayload>(response?.Payload);
    }

    public async Task<IpcStatusPayload?> ResumeAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync("Resume", null, ct);
        return DeserializePayload<IpcStatusPayload>(response?.Payload);
    }

    public async Task<IpcStatusPayload?> ReconnectAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync("Reconnect", null, ct);
        return DeserializePayload<IpcStatusPayload>(response?.Payload);
    }

    public async Task<string?> GetLogPathAsync(CancellationToken ct = default)
    {
        var response = await SendCommandAsync("GetLogPath", null, ct);
        var payload = DeserializePayload<IpcLogPathPayload>(response?.Payload);
        return payload?.Caminho;
    }

    public async Task<IpcEnvelope?> SendCommandAsync(string tipo, object? payload = null, CancellationToken ct = default)
    {
        try
        {
            if (_reader == null || _writer == null)
            {
                var connected = await ConnectAsync(1500, ct);
                if (!connected) return null;
            }

            await _sendLock.WaitAsync(ct);
            try
            {
                var envelope = new
                {
                    versao = 1,
                    tipo = tipo,
                    payload = payload
                };

                var json = JsonSerializer.Serialize(envelope);
                await _writer!.WriteLineAsync(json);
                await _writer.FlushAsync();

                var responseLine = await _reader!.ReadLineAsync(ct);
                if (responseLine == null)
                {
                    await DisconnectAsync();
                    return null;
                }

                var responseEnvelope = JsonSerializer.Deserialize<IpcEnvelope>(responseLine);
                return responseEnvelope;
            }
            finally
            {
                try { _sendLock.Release(); } catch (ObjectDisposedException) { }
            }
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch
        {
            await DisconnectAsync();
            return null;
        }
    }

    private static T? DeserializePayload<T>(JsonElement? element) where T : class
    {
        if (!element.HasValue) return null;
        return JsonSerializer.Deserialize<T>(element.Value.GetRawText());
    }

    public async Task DisconnectAsync()
    {
        if (_writer != null)
        {
            try { await _writer.DisposeAsync(); } catch { }
            _writer = null;
        }

        if (_reader != null)
        {
            try { _reader.Dispose(); } catch { }
            _reader = null;
        }

        if (_pipeClient != null)
        {
            try { await _pipeClient.DisposeAsync(); } catch { }
            _pipeClient = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _sendLock.Dispose();
    }
}
