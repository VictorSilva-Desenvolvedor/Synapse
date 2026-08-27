using System.Text;
using System.Text.Json;
using Shouldly;
using Synapse.Tray.Ipc;

namespace Synapse.Tests.Tray;

public class IpcClientTests
{
    [Fact]
    public async Task GetStatusAsync_WhenServerOffline_ShouldReturnNullGracefully()
    {
        var nonExistentPipe = $"synapse-offline-{Guid.NewGuid():N}";
        await using var client = new IpcClient(nonExistentPipe);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var status = await client.GetStatusAsync(cts.Token);

        status.ShouldBeNull();
        client.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public async Task IpcClient_ShouldProcessStatusResponseCorrectly()
    {
        var responsePayload = new IpcStatusPayload
        {
            Estado = "Sincronizado",
            Pausado = false,
            ItensPendentes = 3,
            UltimaSincronizacaoEm = DateTimeOffset.UtcNow
        };

        var responseJson = JsonSerializer.Serialize(new
        {
            versao = 1,
            tipo = "StatusChanged",
            payload = responsePayload
        });

        await using var stream = new DuplexTestStream(responseJson + "\n");
        await using var client = new IpcClient(stream);

        var result = await client.GetStatusAsync();

        result.ShouldNotBeNull();
        result.Estado.ShouldBe("Sincronizado");
        result.ItensPendentes.ShouldBe(3);
        result.Pausado.ShouldBeFalse();
    }

    [Fact]
    public async Task IpcClient_ShouldProcessPauseResponseCorrectly()
    {
        var responsePayload = new IpcStatusPayload
        {
            Estado = "Sincronizado",
            Pausado = true,
            ItensPendentes = 0
        };

        var responseJson = JsonSerializer.Serialize(new
        {
            versao = 1,
            tipo = "StatusChanged",
            payload = responsePayload
        });

        await using var stream = new DuplexTestStream(responseJson + "\n");
        await using var client = new IpcClient(stream);

        var result = await client.PauseAsync();

        result.ShouldNotBeNull();
        result.Pausado.ShouldBeTrue();
    }

    [Fact]
    public async Task IpcClient_ShouldProcessLogPathResponseCorrectly()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            versao = 1,
            tipo = "LogPath",
            payload = new { caminho = "C:\\Logs\\Synapse" }
        });

        await using var stream = new DuplexTestStream(responseJson + "\n");
        await using var client = new IpcClient(stream);

        var result = await client.GetLogPathAsync();

        result.ShouldBe("C:\\Logs\\Synapse");
    }

    private sealed class DuplexTestStream : Stream
    {
        private readonly MemoryStream _readStream;
        private readonly MemoryStream _writeStream = new();

        public DuplexTestStream(string responseData)
        {
            _readStream = new MemoryStream(Encoding.UTF8.GetBytes(responseData));
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _readStream.Length;
        public override long Position { get => _readStream.Position; set => _readStream.Position = value; }
        public override void Flush() => _writeStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _readStream.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _readStream.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => _writeStream.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _writeStream.WriteAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _readStream.Dispose();
                _writeStream.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
