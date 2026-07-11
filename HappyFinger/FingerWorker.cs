/*
 * Happy Finger Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using JoyfulReaperLib.JRNet;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HappyFinger;

public class FingerWorker(
    ILogger<FingerWorker> logger,
    IOptions<HappyFingerOptions> options) : BackgroundService
{
    private TcpListener? _listener;
    private readonly ConcurrentDictionary<long, Task> _activeConnections = new();
    private volatile bool _stopRequested;
    private readonly SemaphoreSlim _connectionLimit = new(
        options.Value.MaxConcurrentConnections,
        options.Value.MaxConcurrentConnections
    );
    private long _nextConnectionId;
    private static readonly Encoding RequestEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    private static readonly ReadOnlyMemory<byte> ResponseBytes =
        RequestEncoding.GetBytes("You fingered me! How dare you!\r\n");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IPAddress ipAddress = IPAddressUtils.ParseListenAddress(options.Value.ListenAddress);
        _listener = new TcpListener(ipAddress, options.Value.Port);
        _listener.Start();

        logger.LogInformation(
            "HappyFinger Server Listening on {address}:{port}",
            ipAddress,
            options.Value.Port
        );

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException) when (stoppingToken.IsCancellationRequested || _stopRequested)
                {
                    break;
                }
                try
                {
                    await _connectionLimit.WaitAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    client.Dispose();
                    break;
                }

                long connectionId = Interlocked.Increment(ref _nextConnectionId);
                Task task = HandleClientAsync(connectionId, client, stoppingToken);
                _activeConnections[connectionId] = task;

                _ = task.ContinueWith(
                    completedTask =>
                    {
                        _activeConnections.TryRemove(connectionId, out _);
                        _connectionLimit.Release();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            }
        }
        finally
        {
            _listener.Stop();
            Task[] remaining = _activeConnections.Values.ToArray();
            if (remaining.Length > 0)
            {
                try
                {
                    await Task.WhenAll(remaining);
                }
                catch
                {
                    // Normal Shutdown
                }
            }
        }
    }

    private async Task HandleClientAsync(
        long connectionId,
        TcpClient client,
        CancellationToken stoppingToken)
    {
        using (client)
        {
            client.NoDelay = true;
            EndPoint? remote = client.Client.RemoteEndPoint;

            try
            {
                await using NetworkStream stream = client.GetStream();
                string? request = await ReadAsync(stream, options.Value.RequestTimeoutSeconds, stoppingToken);

                logger.LogDebug("Received request: {Request} from {Remote}.", request ?? "<no data>", client.Client.RemoteEndPoint);

                await stream.WriteAsync(ResponseBytes, stoppingToken);
                await stream.FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning(
                    "Connection {ConnectionId} from {Remote} timed out.",
                    connectionId,
                    remote);
            }
            catch (InvalidDataException exception)
            {
                logger.LogWarning(
                    exception,
                    "Rejected malformed request on connection {ConnectionId} from {Remote}.",
                    connectionId,
                    remote);
            }
            catch (IOException exception)
            {
                logger.LogDebug(
                    exception,
                    "Connection {ConnectionId} from {Remote} ended early.",
                    connectionId,
                    remote);
            }
            catch (SocketException exception)
            {
                logger.LogDebug(
                    exception,
                    "Socket error on connection {ConnectionId} from {Remote}.",
                    connectionId,
                    remote);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Unhandled error on connection {ConnectionId} from {Remote}.",
                    connectionId,
                    remote);
            }
        }
    }

    private static async Task<string?> ReadAsync(
        Stream stream,
        int requestTimeoutSeconds,
        CancellationToken stoppingToken)
    {
        const int BUFFER_SIZE = 1024;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(requestTimeoutSeconds));

        try
        {
            int count = 0;
            while (true)
            {
                int remainingBuffer = BUFFER_SIZE - count;
                if (remainingBuffer == 0)
                {
                    throw new InternalBufferOverflowException();
                }

                int bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(count, remainingBuffer),
                    timeout.Token);

                if (bytesRead == 0)
                {
                    return count == 0 ? null : DecodeRequest(buffer, count);
                }

                count += bytesRead;

                // Look for the newline standard (\n or \r\n) to know the client is done typing
                if (buffer.AsSpan(0, count).IndexOf((byte)'\n') >= 0)
                {
                    return DecodeRequest(buffer, count);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string DecodeRequest(byte[] buffer, int length) =>
        RequestEncoding.GetString(buffer, 0, length);

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("HappyFinger Server Stopping...");
        _stopRequested = true;
        _listener?.Stop();

        return base.StopAsync(cancellationToken);
    }
}
