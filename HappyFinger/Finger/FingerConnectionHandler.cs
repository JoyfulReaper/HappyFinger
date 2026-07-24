/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

using HappyFinger.Events;
using HappyFinger.Finger;
using JoyfulReaperLib.MissionControl;
using JoyfulReaperLib.TcpServer;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HappyFinger;

public sealed class FingerConnectionHandler(
    ILogger<FingerConnectionHandler> logger,
    IMissionControlClient missionControlClient,
    IFingerResponseResolver responseResolver,
    IOptions<HappyFingerOptions> options) : ITcpConnectionHandler
{
    private static readonly TimeSpan TelemetryPublishTimeout =
        TimeSpan.FromSeconds(2);

    private static readonly Encoding RequestEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    public async ValueTask HandleAsync(
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        FingerSessionResult? result = await ProcessAsync(
            context.ConnectionId,
            context.Stream,
            context.RemoteEndPoint,
            responseResolver,
            options.Value,
            logger,
            cancellationToken);

        if (result is null)
        {
            return;
        }

        long connectionId = context.ConnectionId;

        context.RegisterAfterClose(afterCloseToken =>
            PublishRequestCompletedTelemetryAsync(
                connectionId,
                result,
                afterCloseToken));
    }

    internal static async Task<FingerSessionResult?> ProcessAsync(
        long connectionId,
        Stream stream,
        EndPoint? remote,
        IFingerResponseResolver responseResolver,
        HappyFingerOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        string correlationId = Guid.NewGuid().ToString("N");

        bool requestReceived = false;
        int requestLength = 0;
        string responseType = FingerResponseTypes.None;
        string outcome = "failed";
        bool succeeded = false;
        bool shouldPublish = true;

        string remoteString = remote?.ToString() ?? "unknown";

        string? remoteAddress = (remote as IPEndPoint)?
            .Address
            .MapToIPv4()
            .ToString();

        bool isIgnoredTelemetrySource =
            !string.IsNullOrWhiteSpace(
                options.TelemetryIgnoredRemoteAddress) &&
            string.Equals(
                remoteAddress,
                options.TelemetryIgnoredRemoteAddress,
                StringComparison.OrdinalIgnoreCase);

        string? request = null;

        try
        {
            request = await ReadAsync(
                stream,
                options.RequestTimeoutSeconds,
                cancellationToken);

            requestReceived = request is not null;

            requestLength = request?
                .TrimEnd('\r', '\n')
                .Length ?? 0;

            logger.LogDebug(
                "Received Finger request containing {RequestLength} characters from {Remote}.",
                requestLength,
                remote);

            FingerResponse response = await responseResolver.ResolveAsync(
                request,
                cancellationToken);

            responseType = response.Type;

            await stream.WriteAsync(
                response.Bytes,
                cancellationToken);

            await stream.FlushAsync(cancellationToken);

            outcome = "served";
            succeeded = true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
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
        finally
        {
            stopwatch.Stop();
        }

        if (!shouldPublish || isIgnoredTelemetrySource)
        {
            logger.LogDebug(
                "Skipping telemetry for health-check connection {ConnectionId} from {Remote}.",
                connectionId,
                remoteString);

            return null;
        }

        return new FingerSessionResult(
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

    private async ValueTask PublishRequestCompletedTelemetryAsync(
        long connectionId,
        FingerSessionResult result,
        CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeout.CancelAfter(TelemetryPublishTimeout);

        try
        {
            bool published = await missionControlClient.TryPublishAsync(
                eventType: FingerRequestCompletedEvent.EventName,
                payload: new FingerRequestCompletedEvent(
                    RequestReceived: result.RequestReceived,
                    Request: result.Request,
                    RequestLength: result.RequestLength,
                    Remote: result.Remote,
                    ResponseType: result.ResponseType,
                    DurationMilliseconds: result.DurationMilliseconds,
                    Outcome: result.Outcome,
                    Succeeded: result.Succeeded),
                payloadTypeInfo:
                    FingerJsonContext.Default.FingerRequestCompletedEvent,
                occurredAt: result.OccurredAt,
                correlationId: result.CorrelationId,
                cancellationToken: timeout.Token);

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control did not accept telemetry for Finger connection {ConnectionId}.",
                    connectionId);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
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
            logger.LogWarning(
                exception,
                "Failed to publish Mission Control event for connection {ConnectionId}.",
                connectionId);
        }
    }

    private static async Task<string?> ReadAsync(
        Stream stream,
        int requestTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        const int BUFFER_SIZE = 1024;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeout.CancelAfter(
            TimeSpan.FromSeconds(requestTimeoutSeconds));

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
                    return count == 0
                        ? null
                        : DecodeRequest(buffer, count);
                }

                count += bytesRead;

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

    private static string DecodeRequest(
        byte[] buffer,
        int length) =>
        RequestEncoding.GetString(buffer, 0, length);

    internal static string SanitizeTelemetryRequest(
        string? request)
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

                UnicodeCategory category =
                    char.GetUnicodeCategory(character);

                if (category is
                    UnicodeCategory.Control or
                    UnicodeCategory.Format)
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
}

internal sealed record FingerSessionResult(
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