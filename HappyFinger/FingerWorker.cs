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
using System.Net;
using System.Net.Sockets;

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
        FingerSessionResult? result =
            await FingerConnectionHandler.ProcessAsync(
                connectionId,
                stream,
                remote,
                responseResolver,
                options.Value,
                logger,
                stoppingToken);

        return result is null
            ? null
            : new FingerRequestTelemetryResult(
                RequestReceived: result.RequestReceived,
                RequestLength: result.RequestLength,
                Request: result.Request,
                Remote: result.Remote,
                ResponseType: result.ResponseType,
                DurationMilliseconds: result.DurationMilliseconds,
                Outcome: result.Outcome,
                Succeeded: result.Succeeded,
                OccurredAt: result.OccurredAt,
                CorrelationId: result.CorrelationId);
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

    internal static string SanitizeTelemetryRequest(
        string? request) =>
            FingerConnectionHandler.SanitizeTelemetryRequest(request);

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
