/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyFinger.Events;
using JoyfulReaperLib.JRNet;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;

namespace HappyFinger;

public sealed class FingerLifecycleService(
    ILogger<FingerLifecycleService> logger,
    IMissionControlClient missionControlClient,
    IOptions<HappyFingerOptions> options) : IHostedLifecycleService
{
    private static readonly TimeSpan TelemetryPublishTimeout = TimeSpan.FromSeconds(2);

    public Task StartingAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        var listenAddress = IPAddressUtils.ParseListenAddress(options.Value.ListenAddress);

        logger.LogInformation(
            "HappyFinger server started on {Address}:{Port}",
            listenAddress,
            options.Value.Port);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TelemetryPublishTimeout);

        try
        {
            bool published =
                await missionControlClient.TryPublishAsync(
                    eventType: FingerServiceStartedEvent.EventName,
                    payload: new FingerServiceStartedEvent(
                        $"{listenAddress}:{options.Value.Port}"),
                    payloadTypeInfo:
                        FingerJsonContext.Default.FingerServiceStartedEvent,
                    occurredAt: DateTimeOffset.UtcNow,
                    correlationId: null,
                    cancellationToken: timeout.Token);

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control did not accept {EventType}",
                    FingerServiceStartedEvent.EventName);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
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
                "Failed to publish Mission Control event for Finger Service Started.");
        }
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "HappyFinger service stopping...");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}