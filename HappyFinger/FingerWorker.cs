/*
 * Happy Finger Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using JoyfulReaperLib.JRNet;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HappyFinger;

public class FingerWorker(
    ILogger<FingerWorker> logger,
    IMissionControlClient missionControlClient,
    IFingerResponseResolver responseResolver,
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
            await using NetworkStream stream = client.GetStream();

            await HandleConnectionAsync(
                connectionId,
                stream,
                client.Client.RemoteEndPoint,
                stoppingToken);
        }
    }

    internal async Task HandleConnectionAsync(
        long connectionId,
        Stream stream,
        EndPoint? remote,
        CancellationToken stoppingToken)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N");

        bool requestReceived = false;
        int requestLength = 0;
        string responseType = FingerResponseTypes.None;
        string outcome = "failed";
        bool succeeded = false;
        bool shouldPublish = true;

        string remoteString = remote?.ToString() ?? "unknown";
        var remoteAddress = (remote as IPEndPoint)?
            .Address
            .MapToIPv4()
            .ToString();

        bool isIgnoredTelemetrySource =
            !string.IsNullOrWhiteSpace(
                options.Value.TelemetryIgnoredRemoteAddress) &&
            string.Equals(
                remoteAddress,
                options.Value.TelemetryIgnoredRemoteAddress,
                StringComparison.OrdinalIgnoreCase);

        try
        {
            string? request = await ReadAsync(stream, options.Value.RequestTimeoutSeconds, stoppingToken);

            requestReceived = request is not null;

            requestLength = request?
                .TrimEnd('\r', '\n')
                .Length ?? 0;

            logger.LogDebug(
                "Received Finger request containing {RequestLength} characters from {Remote}.",
                requestLength,
                remote);

            FingerResponse response =
                await responseResolver.ResolveAsync(
                    request,
                    stoppingToken);

            responseType = response.Type;

            await stream.WriteAsync(response.Bytes, stoppingToken);
            await stream.FlushAsync(stoppingToken);

            outcome = "served";
            succeeded = true;
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // The application is shutting down. This is not a request
            // timeout and does not need to produce a telemetry event.
            shouldPublish = false;

            logger.LogDebug(
                "Connection {ConnectionId} from {Remote} was cancelled during shutdown.",
                connectionId,
                remote);
        }
        catch (OperationCanceledException)
        {
            outcome = "timeout";

            logger.LogWarning(
                "Connection {ConnectionId} from {Remote} timed out.",
                connectionId,
                remote);
        }
        catch (InvalidDataException exception)
        {
            outcome = "malformed";

            logger.LogWarning(
                exception,
                "Rejected malformed request on connection {ConnectionId} from {Remote}.",
                connectionId,
                remote);
        }
        catch (IOException exception)
        {
            outcome = "io-error";

            logger.LogDebug(
                exception,
                "Connection {ConnectionId} from {Remote} ended early.",
                connectionId,
                remote);
        }
        catch (SocketException exception)
        {
            outcome = "socket-error";

            logger.LogDebug(
                exception,
                "Socket error on connection {ConnectionId} from {Remote}.",
                connectionId,
                remote);
        }
        catch (Exception exception)
        {
            outcome = "failed";

            logger.LogError(
                exception,
                "Unhandled error on connection {ConnectionId} from {Remote}.",
                connectionId,
                remote);
        }

        stopwatch.Stop();

        if (!shouldPublish || isIgnoredTelemetrySource)
        {
            logger.LogDebug(
                "Skipping telemetry for health-check connection {ConnectionId} from {Remote}.",
                connectionId,
                remoteString);

            return;
        }

        try
        {
            await missionControlClient.TryPublishAsync(
                eventType: "happyfinger.request.completed",
                payload: new FingerRequestCompletedEvent(
                    RequestReceived: requestReceived,
                    RequestLength: requestLength,
                    Remote: remoteString,
                    ResponseType: responseType,
                    DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                    Outcome: outcome,
                    Succeeded: succeeded),
                occurredAt: occurredAt,
                correlationId: correlationId,
                cancellationToken: stoppingToken);
        }
        catch (Exception exception)
        {
            // IMissionControlClient is supposed to be best-effort, but this
            // protects HappyFinger from custom or test implementations that throw.
            logger.LogWarning(
                exception,
                "Failed to publish Mission Control event for connection {ConnectionId}.",
                connectionId);
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
                    throw new InvalidDataException(
                        $"Finger request exceeded the maximum supported length of {BUFFER_SIZE} bytes.");
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
