using Shouldly;
using Synapse.Host.Ipc;

namespace Synapse.Tests.Host;

public class IpcServerTests
{
    [Fact]
    public async Task ProcessCommandAsync_WhenGetStatus_ShouldReturnStatusPayload()
    {
        var status = "Sincronizado";
        var isPaused = false;

        var server = new IpcServer(
            getStatusHandler: () => new IpcStatusPayload
            {
                Estado = status,
                Pausado = isPaused,
                ItensPendentes = 5
            },
            pauseHandler: () => Task.FromResult(new IpcStatusPayload { Estado = status, Pausado = true }),
            resumeHandler: () => Task.FromResult(new IpcStatusPayload { Estado = status, Pausado = false }),
            reconnectHandler: () => Task.FromResult(new IpcStatusPayload { Estado = status }),
            getLogPathHandler: () => "C:\\Logs\\Synapse");

        var response = await server.ProcessCommandAsync(new IpcEnvelope { Tipo = "GetStatus" });

        response.ShouldNotBeNull();
        response.Tipo.ShouldBe("StatusChanged");
        response.Payload.ShouldBeOfType<IpcStatusPayload>();
        var payload = (IpcStatusPayload)response.Payload!;
        payload.Estado.ShouldBe("Sincronizado");
        payload.ItensPendentes.ShouldBe(5);
        payload.Pausado.ShouldBeFalse();
    }

    [Fact]
    public async Task ProcessCommandAsync_WhenPause_ShouldCallPauseHandler()
    {
        var isPaused = false;

        var server = new IpcServer(
            getStatusHandler: () => new IpcStatusPayload(),
            pauseHandler: () =>
            {
                isPaused = true;
                return Task.FromResult(new IpcStatusPayload { Pausado = true });
            },
            resumeHandler: () => Task.FromResult(new IpcStatusPayload { Pausado = false }),
            reconnectHandler: () => Task.FromResult(new IpcStatusPayload()),
            getLogPathHandler: () => "C:\\Logs\\Synapse");

        var response = await server.ProcessCommandAsync(new IpcEnvelope { Tipo = "Pause" });

        isPaused.ShouldBeTrue();
        response.Tipo.ShouldBe("StatusChanged");
        ((IpcStatusPayload)response.Payload!).Pausado.ShouldBeTrue();
    }

    [Fact]
    public async Task ProcessCommandAsync_WhenResume_ShouldCallResumeHandler()
    {
        var isPaused = true;

        var server = new IpcServer(
            getStatusHandler: () => new IpcStatusPayload(),
            pauseHandler: () => Task.FromResult(new IpcStatusPayload { Pausado = true }),
            resumeHandler: () =>
            {
                isPaused = false;
                return Task.FromResult(new IpcStatusPayload { Pausado = false });
            },
            reconnectHandler: () => Task.FromResult(new IpcStatusPayload()),
            getLogPathHandler: () => "C:\\Logs\\Synapse");

        var response = await server.ProcessCommandAsync(new IpcEnvelope { Tipo = "Resume" });

        isPaused.ShouldBeFalse();
        response.Tipo.ShouldBe("StatusChanged");
        ((IpcStatusPayload)response.Payload!).Pausado.ShouldBeFalse();
    }

    [Fact]
    public async Task ProcessCommandAsync_WhenGetLogPath_ShouldReturnLogPath()
    {
        var server = new IpcServer(
            getStatusHandler: () => new IpcStatusPayload(),
            pauseHandler: () => Task.FromResult(new IpcStatusPayload()),
            resumeHandler: () => Task.FromResult(new IpcStatusPayload()),
            reconnectHandler: () => Task.FromResult(new IpcStatusPayload()),
            getLogPathHandler: () => "C:\\Logs\\Synapse");

        var response = await server.ProcessCommandAsync(new IpcEnvelope { Tipo = "GetLogPath" });

        response.Tipo.ShouldBe("LogPath");
        var logPayload = response.Payload.ShouldBeOfType<IpcLogPathPayload>();
        logPayload.Caminho.ShouldBe("C:\\Logs\\Synapse");
    }

    [Fact]
    public async Task ProcessCommandAsync_WhenUnknownCommand_ShouldReturnError()
    {
        var server = new IpcServer(
            getStatusHandler: () => new IpcStatusPayload(),
            pauseHandler: () => Task.FromResult(new IpcStatusPayload()),
            resumeHandler: () => Task.FromResult(new IpcStatusPayload()),
            reconnectHandler: () => Task.FromResult(new IpcStatusPayload()),
            getLogPathHandler: () => "C:\\Logs\\Synapse");

        var response = await server.ProcessCommandAsync(new IpcEnvelope { Tipo = "ComandoInexistente" });

        response.Tipo.ShouldBe("Error");
    }

    [Fact(Timeout = 5000)]
    public async Task StartAsync_WhenFirstClientHoldsConnection_SecondClientShouldConnectAndReceiveResponse()
    {
        var pipeName = $"synapse-ipc-test-{Guid.NewGuid():N}";
        var server = new IpcServer(
            getStatusHandler: () => new IpcStatusPayload { Estado = "Sincronizado" },
            pauseHandler: () => Task.FromResult(new IpcStatusPayload()),
            resumeHandler: () => Task.FromResult(new IpcStatusPayload()),
            reconnectHandler: () => Task.FromResult(new IpcStatusPayload()),
            getLogPathHandler: () => "C:\\Logs\\Synapse",
            pipeName: pipeName);

        using var serverCts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.StartAsync(serverCts.Token));

        await Task.Delay(100);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var ct = timeoutCts.Token;

        try
        {
            // 1. Cliente 1 conecta e envia comando, mantendo a conexão aberta
            using var client1 = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
            await client1.ConnectAsync(ct);
            client1.IsConnected.ShouldBeTrue();

            using var writer1 = new StreamWriter(client1, new System.Text.UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader1 = new StreamReader(client1, new System.Text.UTF8Encoding(false), leaveOpen: true);
            await writer1.WriteLineAsync("{\"versao\":1,\"tipo\":\"GetStatus\",\"payload\":null}");
            await writer1.FlushAsync();
            var response1 = await reader1.ReadLineAsync(ct);
            response1.ShouldNotBeNull();
            response1.ShouldContain("Sincronizado");

            // 2. Cliente 2 conecta enquanto Cliente 1 ainda está com conexão aberta
            using var client2 = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
            await client2.ConnectAsync(ct);
            client2.IsConnected.ShouldBeTrue();

            using var writer2 = new StreamWriter(client2, new System.Text.UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader2 = new StreamReader(client2, new System.Text.UTF8Encoding(false), leaveOpen: true);
            await writer2.WriteLineAsync("{\"versao\":1,\"tipo\":\"GetStatus\",\"payload\":null}");
            await writer2.FlushAsync();
            var response2 = await reader2.ReadLineAsync(ct);
            response2.ShouldNotBeNull();
            response2.ShouldContain("Sincronizado");

            // 3. Cliente 1 continua vivo e funcional na conexão persistente
            await writer1.WriteLineAsync("{\"versao\":1,\"tipo\":\"GetStatus\",\"payload\":null}");
            await writer1.FlushAsync();
            var response1Again = await reader1.ReadLineAsync(ct);
            response1Again.ShouldNotBeNull();
            response1Again.ShouldContain("Sincronizado");
        }
        finally
        {
            serverCts.Cancel();
            try { await serverTask; } catch { }
        }
    }
}
