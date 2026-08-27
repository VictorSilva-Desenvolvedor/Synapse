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
}
