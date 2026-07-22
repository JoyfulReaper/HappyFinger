/*
 * Happy Finger Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyFinger.Events;
using HappyFinger.Finger;
using JoyfulReaperLib.JRNet;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
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
    private static readonly TimeSpan TelemetryPublishTimeout =
        TimeSpan.FromSeconds(2);

    private TcpListener? _listener;
    private readonly ConcurrentDictionary<long, Task> _activeConnections = new();
    private volatile bool _stopRequested;
    private readonly SemaphoreSlim _connectionLimit = new(
        options.Value.MaxConcurrentConnections,
        options.Value.MaxConcurrentConnections
    );
    private long _nextConnectionId;
    public int BoundPort { get; private set; }

    private static readonly Encoding RequestEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IPAddress ipAddress = IPAddressUtils.ParseListenAddress(options.Value.ListenAddress);
        _listener = new TcpListener(ipAddress, options.Value.Port);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

        logger.LogInformation(
            "HappyFinger Server Listening on {address}:{port}",
            ipAddress,
            options.Value.Port
        );

        var occurredAt = DateTimeOffset.UtcNow;

        await PublishServiceStartedTelemetryAsync(
            $"{ipAddress}:{options.Value.Port}",
            occurredAt,
            stoppingToken);

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
        FingerRequestTelemetryResult? telemetry = null;

        using (client)
        {
            try
            {
                client.NoDelay = true;
                await using NetworkStream stream = client.GetStream();

                telemetry = await HandleConnectionAsync(
                    connectionId,
                    stream,
                    client.Client.RemoteEndPoint,
                    stoppingToken);
            }
            finally
            {
                _connectionLimit.Release();
            }
        }

        if (telemetry is not null)
        {
            await PublishRequestCompletedTelemetryAsync(
                connectionId,
                telemetry,
                stoppingToken);
        }
    }

    internal async Task<FingerRequestTelemetryResult?> HandleConnectionAsync(
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

        string? request = null;
        try
        {
            request = await ReadAsync(stream, options.Value.RequestTimeoutSeconds, stoppingToken);

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

            return null;
        }

        return new FingerRequestTelemetryResult(
            RequestReceived: requestReceived,
            RequestLength: requestLength,
            Request: SanitizeTelemetryRequest(request),
            Remote: remoteString,
            ResponseType: responseType,
            DurationMilliseconds: stopwatch.ElapsedMilliseconds,
            Outcome: outcome,
            Succeeded: succeeded,
            OccurredAt: occurredAt,
            CorrelationId: correlationId);
    }

    private async Task PublishServiceStartedTelemetryAsync(
        string endpoint,
        DateTimeOffset occurredAt,
        CancellationToken stoppingToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TelemetryPublishTimeout);

        try
        {
            bool published = await missionControlClient.TryPublishAsync(
                eventType: FingerServiceStartedEvent.EventName,
                payload: new FingerServiceStartedEvent(endpoint),
                payloadTypeInfo: FingerJsonContext.Default.FingerServiceStartedEvent,
                occurredAt: occurredAt,
                correlationId: null,
                cancellationToken: timeout.Token);

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control did not accept {EventType}",
                    FingerServiceStartedEvent.EventName);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Service-started telemetry publishing stopped during shutdown.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Timed out publishing Mission Control event for Finger Service Started.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish Mission Control event for Finger Service Started");
        }
    }

    private async Task PublishRequestCompletedTelemetryAsync(
        long connectionId,
        FingerRequestTelemetryResult telemetry,
        CancellationToken stoppingToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TelemetryPublishTimeout);

        try
        {
            bool published = await missionControlClient.TryPublishAsync(
                eventType: "happyfinger.request.completed",
                payload: new FingerRequestCompletedEvent(
                    telemetry.RequestReceived,
                    telemetry.Request,
                    telemetry.RequestLength,
                    telemetry.Remote,
                    telemetry.ResponseType,
                    telemetry.DurationMilliseconds,
                    telemetry.Outcome,
                    telemetry.Succeeded),
                payloadTypeInfo: FingerJsonContext.Default.FingerRequestCompletedEvent,
                occurredAt: telemetry.OccurredAt,
                correlationId: telemetry.CorrelationId,
                cancellationToken: timeout.Token);

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control did not accept telemetry for Finger connection {ConnectionId}.",
                    connectionId);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Telemetry publishing stopped for Finger connection {ConnectionId}.",
                connectionId);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Timed out publishing telemetry for Finger connection {ConnectionId}.",
                connectionId);
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

    internal static string SanitizeTelemetryRequest(string? request)
    {
        const int MAX_TELEMETRY_REQUEST_LENGTH = 100;

        if (string.IsNullOrEmpty(request))
        {
            return string.Empty;
        }

        try
        {
            var sanitized = new StringBuilder(request.Length);
            foreach (char character in request)
            {
                if (character is '\r' or '\n')
                {
                    continue;
                }

                if (character == '\t')
                {
                    sanitized.Append(' ');
                    continue;
                }

                UnicodeCategory category = char.GetUnicodeCategory(character);
                if (category is UnicodeCategory.Control or UnicodeCategory.Format)
                {
                    continue;
                }

                sanitized.Append(character);
            }

            string value = sanitized.ToString().Trim();
            return value.Length > MAX_TELEMETRY_REQUEST_LENGTH
                ? value[..MAX_TELEMETRY_REQUEST_LENGTH] + "..."
                : value;
        }
        catch
        {
            return string.Empty;
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("HappyFinger Server Stopping...");
        _stopRequested = true;
        _listener?.Stop();

        return base.StopAsync(cancellationToken);
    }

    internal sealed record FingerRequestTelemetryResult(
        bool RequestReceived,
        int RequestLength,
        string Request,
        string Remote,
        string ResponseType,
        long DurationMilliseconds,
        string Outcome,
        bool Succeeded,
        DateTimeOffset OccurredAt,
        string CorrelationId);
}
