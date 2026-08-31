using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace Launcher.Core.Services;

public sealed class UbRelayClient : IAsyncDisposable
{
    private const string RelayHost = "213.35.106.78";
    private const int RelayControlPort = 28080;
    private const string RelayToken = "e180334b26744649a87721236b467cb9812a5803f40b4b01b6822cd48dba2bda";
    private const int MaximumFrameSize = 1024 * 1024;

    private readonly ConcurrentDictionary<uint, TcpClient> localConnections = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly CancellationTokenSource shutdown = new();
    private TcpClient? controlClient;
    private NetworkStream? controlStream;
    private Task? receiveTask;
    private int localPort;
    private int stopped;

    public int PublicPort { get; private set; }

    public string PublicAddress => $"{RelayHost}:{PublicPort}";

    public event Action<string>? Disconnected;

    public async Task StartAsync(int minecraftServerPort, CancellationToken cancellationToken = default)
    {
        if (controlClient is not null)
        {
            throw new InvalidOperationException("The relay is already running.");
        }

        localPort = minecraftServerPort;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
        linkedCancellation.CancelAfter(TimeSpan.FromSeconds(15));

        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(RelayHost, RelayControlPort, linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new IOException("UB Relay is unreachable. Oracle port 28080/TCP is probably blocked.");
        }
        var stream = client.GetStream();
        var handshake = Encoding.UTF8.GetBytes($"UBR1 {RelayToken}\n");
        await stream.WriteAsync(handshake, linkedCancellation.Token);

        var response = await ReadLineAsync(stream, 128, linkedCancellation.Token);
        if (!response.StartsWith("PORT ", StringComparison.Ordinal) ||
            !int.TryParse(response.AsSpan(5), out var assignedPort) ||
            assignedPort is < 1024 or > 65535)
        {
            client.Dispose();
            throw new InvalidOperationException(response.StartsWith("ERROR ", StringComparison.Ordinal)
                ? $"Relay rejected the connection: {response[6..]}"
                : "The relay returned an invalid public port.");
        }

        controlClient = client;
        controlStream = stream;
        PublicPort = assignedPort;
        receiveTask = Task.Run(() => ReceiveFramesAsync(shutdown.Token));
    }

    private async Task ReceiveFramesAsync(CancellationToken cancellationToken)
    {
        var header = new byte[9];
        try
        {
            while (!cancellationToken.IsCancellationRequested && controlStream is not null)
            {
                await ReadExactlyAsync(controlStream, header, cancellationToken);
                var frameType = header[0];
                var connectionId = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1, 4));
                var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(5, 4));
                if (payloadLength > MaximumFrameSize)
                {
                    throw new IOException("The relay sent an oversized frame.");
                }

                var payload = payloadLength == 0 ? [] : new byte[(int)payloadLength];
                if (payload.Length > 0)
                {
                    await ReadExactlyAsync(controlStream, payload, cancellationToken);
                }

                switch (frameType)
                {
                    case 1:
                        await OpenLocalConnectionAsync(connectionId, cancellationToken);
                        break;
                    case 2:
                        if (localConnections.TryGetValue(connectionId, out var localClient))
                        {
                            await localClient.GetStream().WriteAsync(payload, cancellationToken);
                        }
                        break;
                    case 3:
                        CloseLocalConnection(connectionId, false);
                        break;
                    case 5:
                        break;
                    default:
                        throw new IOException($"Unknown relay frame type {frameType}.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await StopInternalAsync($"Relay disconnected: {exception.Message}", notify: true);
        }
    }

    private async Task OpenLocalConnectionAsync(uint connectionId, CancellationToken cancellationToken)
    {
        var localClient = new TcpClient { NoDelay = true };
        try
        {
            await localClient.ConnectAsync("127.0.0.1", localPort, cancellationToken);
            if (!localConnections.TryAdd(connectionId, localClient))
            {
                localClient.Dispose();
                return;
            }

            _ = PumpLocalConnectionAsync(connectionId, localClient, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            localClient.Dispose();
        }
        catch
        {
            localClient.Dispose();
            await SendFrameAsync(3, connectionId, ReadOnlyMemory<byte>.Empty, cancellationToken);
        }
    }

    private async Task PumpLocalConnectionAsync(
        uint connectionId,
        TcpClient localClient,
        CancellationToken cancellationToken)
    {
        try
        {
            await PumpLocalToRelayAsync(connectionId, localClient, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
        }
        finally
        {
            CloseLocalConnection(connectionId, true);
        }
    }

    private async Task PumpLocalToRelayAsync(uint connectionId, TcpClient localClient, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var localStream = localClient.GetStream();
        while (!cancellationToken.IsCancellationRequested)
        {
            var bytesRead = await localStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                return;
            }

            await SendFrameAsync(2, connectionId, buffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }

    private async Task SendFrameAsync(
        byte frameType,
        uint connectionId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var stream = controlStream;
        if (stream is null || payload.Length > MaximumFrameSize)
        {
            return;
        }

        var header = new byte[9];
        header[0] = frameType;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(1, 4), connectionId);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(5, 4), (uint)payload.Length);

        await sendLock.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(header, cancellationToken);
            if (!payload.IsEmpty)
            {
                await stream.WriteAsync(payload, cancellationToken);
            }
        }
        finally
        {
            sendLock.Release();
        }
    }

    private void CloseLocalConnection(uint connectionId, bool notifyRelay)
    {
        if (!localConnections.TryRemove(connectionId, out var client))
        {
            return;
        }

        client.Dispose();
        if (notifyRelay && !shutdown.IsCancellationRequested)
        {
            _ = SendFrameAsync(3, connectionId, ReadOnlyMemory<byte>.Empty, shutdown.Token);
        }
    }

    public async Task StopAsync()
    {
        await StopInternalAsync("Relay stopped", notify: false);
    }

    private async Task StopInternalAsync(string reason, bool notify)
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0)
        {
            return;
        }

        shutdown.Cancel();
        foreach (var connection in localConnections.Values)
        {
            connection.Dispose();
        }
        localConnections.Clear();

        controlClient?.Dispose();
        controlClient = null;
        controlStream = null;

        if (!notify && receiveTask is not null)
        {
            try
            {
                await receiveTask;
            }
            catch
            {
            }
        }

        if (notify)
        {
            Disconnected?.Invoke(reason);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        sendLock.Dispose();
        shutdown.Dispose();
    }

    private static async Task<string> ReadLineAsync(
        NetworkStream stream,
        int maximumLength,
        CancellationToken cancellationToken)
    {
        using var line = new MemoryStream();
        var singleByte = new byte[1];
        while (line.Length < maximumLength)
        {
            var read = await stream.ReadAsync(singleByte, cancellationToken);
            if (read == 0)
            {
                throw new IOException("Relay closed before assigning a port.");
            }
            if (singleByte[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r');
            }
            line.WriteByte(singleByte[0]);
        }

        throw new IOException("Relay response was too long.");
    }

    private static async Task ReadExactlyAsync(
        NetworkStream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                throw new IOException("Relay connection closed.");
            }
            offset += read;
        }
    }
}
