using HappyFinger.Events;
using HappyFinger.Finger;
using HappyFinger.Plan;
using HappyFinger.Steam;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HappyFinger.Tests;

public sealed class FingerWorkerTelemetryTests
{
    public static TheoryData<string, string> SuccessfulRequests => new()
    {
        { "\r\n", FingerResponseTypes.Directory },
        { "kyle\r\n", FingerResponseTypes.Kyle },
        { "unknown\r\n", FingerResponseTypes.NotFound },
        { "/Wrong\r\n", FingerResponseTypes.NotFound },
        { "name@host\r\n", FingerResponseTypes.ForwardingNotSupported },
        { "joke\r\n", FingerResponseTypes.Joke },
        { "now\r\n", FingerResponseTypes.Now }
    };

    public static TheoryData<string?, string> TelemetryRequestSanitizationCases => new()
    {
        { "kyle", "kyle" },
        { "76561198000000000", "76561198000000000" },
        { "ky\r\nle", "kyle" },
        { "ky\tle", "ky le" },
        { "  kyle  ", "kyle" },
        { "ky\u0001le", "kyle" },
        { "ky\u200Ele", "kyle" },
        { "kyle \u2603", "kyle \u2603" },
        { new string('x', 100), new string('x', 100) },
        { new string('x', 101), new string('x', 100) + "..." },
        { null, string.Empty }
    };

    [Theory]
    [MemberData(nameof(TelemetryRequestSanitizationCases))]
    public void SanitizeTelemetryRequest_ReturnsExpectedRequest(
        string? request,
        string expected) =>
        Assert.Equal(expected, FingerWorker.SanitizeTelemetryRequest(request));

    [Theory]
    [MemberData(nameof(SuccessfulRequests))]
    public async Task HandleConnectionAsync_PublishesSelectedResponseTypeForSuccessfulRequest(
        string request,
        string expectedResponseType)
    {
        var missionControlClient = new TestMissionControlClient();
        FingerWorker worker = CreateWorker(
            missionControlClient,
            responseResolver: CreateResolver(
                new PlanFileResult(
                    Available: true,
                    Content: "public plan content",
                    Truncated: false)));
        var stream = new ScriptedStream(Encoding.UTF8.GetBytes(request));

        var payload = AssertTelemetryPayload(await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None));

        Assert.Equal(expectedResponseType, payload.ResponseType);
        Assert.Equal("served", payload.Outcome);
        Assert.True(payload.Succeeded);
        Assert.Equal(1, stream.WriteCount);
        Assert.Equal(1, stream.FlushCount);
    }

    [Fact]
    public async Task HandleConnectionAsync_FileBackedNowTelemetryDoesNotIncludePlanContentOrPath()
    {
        var missionControlClient = new TestMissionControlClient();
        FingerWorker worker = CreateWorker(
            missionControlClient,
            responseResolver: CreateResolver(
                new PlanFileResult(
                    Available: true,
                    Content: "private plan detail",
                    Truncated: false)));
        var stream = new ScriptedStream(Encoding.UTF8.GetBytes("now\r\n"));

        var payload = AssertTelemetryPayload(await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None));

        string payloadText = payload.ToString();

        Assert.Equal(FingerResponseTypes.Now, payload.ResponseType);
        Assert.Equal("served", payload.Outcome);
        Assert.True(payload.Succeeded);
        Assert.DoesNotContain("private plan detail", payloadText);
        Assert.DoesNotContain("data/.plan", payloadText);
    }

    [Fact]
    public async Task HandleConnectionAsync_RandomGamePublishesOnlyControlledTelemetry()
    {
        var missionControlClient = new TestMissionControlClient();
        FingerWorker worker = CreateWorker(
            missionControlClient,
            responseResolver: CreateResolver(
                randomSteamGameResult: new RandomSteamGameResult(
                    Succeeded: true,
                    Game: new RandomGameDetails
                    {
                        Id = 42424242,
                        Name = "Half-Life 2",
                        PlaytimeForever = 872,
                        RTimeLastPlayed = 1762204440
                    })));
        var stream = new ScriptedStream(
            Encoding.UTF8.GetBytes("76561198000000000\r\n"));

        var payload = AssertTelemetryPayload(await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None));

        string payloadText = SerializePayload(payload);

        Assert.Equal(FingerResponseTypes.RandomGame, payload.ResponseType);
        Assert.Equal("76561198000000000", payload.Request);
        Assert.Equal(17, payload.RequestLength);
        Assert.Equal("served", payload.Outcome);
        Assert.True(payload.Succeeded);
        Assert.DoesNotContain("Half-Life", payloadText);
        Assert.DoesNotContain("42424242", payloadText);
        Assert.DoesNotContain("\"id\"", payloadText);
        Assert.DoesNotContain("\"name\"", payloadText);
        Assert.DoesNotContain("\"playtimeForever\"", payloadText);
        Assert.DoesNotContain("\"rTimeLastPlayed\"", payloadText);
        Assert.DoesNotContain("872", payloadText);
        Assert.DoesNotContain("1762204440", payloadText);
        Assert.DoesNotContain("randomsteam.kgivler.com", payloadText);
    }

    [Fact]
    public async Task HandleConnectionAsync_SanitizesTelemetryRequestWithoutChangingProtocolRequest()
    {
        var missionControlClient = new TestMissionControlClient();
        var responseResolver = new RecordingResponseResolver();
        FingerWorker worker = CreateWorker(
            missionControlClient,
            responseResolver: responseResolver);
        var stream = new ScriptedStream(
            Encoding.UTF8.GetBytes("  ky\tle\u0001\r\n"));

        var payload = AssertTelemetryPayload(await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None));

        Assert.Equal("ky le", payload.Request);
        Assert.Equal("  ky\tle\u0001\r\n", responseResolver.Request);
        Assert.Equal("resolver response", Encoding.UTF8.GetString(stream.WrittenBytes));
    }

    [Fact]
    public async Task HandleConnectionAsync_RandomGameUnavailablePublishesServedTelemetry()
    {
        var missionControlClient = new TestMissionControlClient();
        FingerWorker worker = CreateWorker(
            missionControlClient,
            responseResolver: CreateResolver(
                randomSteamGameResult: new RandomSteamGameResult(
                    Succeeded: false,
                    Game: null)));
        var stream = new ScriptedStream(
            Encoding.UTF8.GetBytes("76561198000000000\r\n"));

        var payload = AssertTelemetryPayload(await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None));

        Assert.Equal(FingerResponseTypes.RandomGameUnavailable, payload.ResponseType);
        Assert.Equal("served", payload.Outcome);
        Assert.True(payload.Succeeded);
    }

    [Fact]
    public async Task HandleConnectionAsync_TimeoutBeforeRoutingReportsNone()
    {
        var missionControlClient = new TestMissionControlClient();
        FingerWorker worker = CreateWorker(missionControlClient);
        var stream = new ThrowingReadStream(new OperationCanceledException());

        var payload = AssertTelemetryPayload(await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None));
        Assert.Equal(FingerResponseTypes.None, payload.ResponseType);
        Assert.Equal("timeout", payload.Outcome);
        Assert.False(payload.Succeeded);
    }

    [Fact]
    public async Task HandleConnectionAsync_MalformedRequestBeforeRoutingReportsNone()
    {
        var missionControlClient = new TestMissionControlClient();
        FingerWorker worker = CreateWorker(missionControlClient);
        var stream = new ScriptedStream(Encoding.UTF8.GetBytes(new string('x', 1024)));

        var payload = AssertTelemetryPayload(await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None));
        Assert.Equal(FingerResponseTypes.None, payload.ResponseType);
        Assert.Equal("malformed", payload.Outcome);
        Assert.False(payload.Succeeded);
    }

    [Fact]
    public async Task HandleConnectionAsync_WriteFailureAfterRoutingKeepsSelectedResponseType()
    {
        var missionControlClient = new TestMissionControlClient();
        FingerWorker worker = CreateWorker(missionControlClient);
        var stream = new ScriptedStream(
            Encoding.UTF8.GetBytes("\r\n"),
            throwOnWrite: new IOException("Simulated write failure."));

        var payload = AssertTelemetryPayload(await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None));
        Assert.Equal(FingerResponseTypes.Directory, payload.ResponseType);
        Assert.Equal("io-error", payload.Outcome);
        Assert.False(payload.Succeeded);
    }

    [Fact]
    public async Task HandleConnectionAsync_TelemetryFailureDoesNotBreakRequestProcessing()
    {
        var missionControlClient = new TestMissionControlClient
        {
            ThrowOnPublish = true
        };
        FingerWorker worker = CreateWorker(missionControlClient);
        var stream = new ScriptedStream(Encoding.UTF8.GetBytes("kyle\r\n"));

        _ = await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None);

        Assert.Contains(
            "Kyle content",
            Encoding.UTF8.GetString(stream.WrittenBytes));
    }

    [Fact]
    public async Task HandleConnectionAsync_IgnoredTelemetrySourcePublishesNoEvent()
    {
        var missionControlClient = new TestMissionControlClient();
        FingerWorker worker = CreateWorker(
            missionControlClient,
            new HappyFingerOptions
            {
                TelemetryIgnoredRemoteAddress = "203.0.113.10"
            });
        var stream = new ScriptedStream(Encoding.UTF8.GetBytes("kyle\r\n"));

        var telemetry = await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            new IPEndPoint(IPAddress.Parse("203.0.113.10"), 54321),
            CancellationToken.None);

        Assert.Null(telemetry);
    }

    [Fact]
    public async Task HandleConnectionAsync_ShutdownCancellationPublishesNoEvent()
    {
        var missionControlClient = new TestMissionControlClient();
        FingerWorker worker = CreateWorker(missionControlClient);
        var stream = new ThrowingReadStream(new OperationCanceledException());
        using var stoppingTokenSource = new CancellationTokenSource();
        await stoppingTokenSource.CancelAsync();

        var telemetry = await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            stoppingTokenSource.Token);

        Assert.Null(telemetry);
    }

    [Fact]
    public async Task ClientReceivesEofWhileRequestTelemetryIsBlocked()
    {
        var missionControl = new BlockingRequestMissionControlClient();
        await using var server = await FingerServerHarness.StartAsync(missionControl);

        string response = await ReadFingerResponseAsync(server.Port, "kyle\r\n");
        await missionControl.WaitForStartedCountAsync(1, TimeSpan.FromSeconds(2));

        Assert.Contains("Kyle content", response);
        Assert.Equal(0, missionControl.FinishedCount);

        missionControl.Release();
        await missionControl.WaitForFinishedCountAsync(1, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConnectionSlotIsReleasedBeforeRequestTelemetryCompletes()
    {
        var missionControl = new BlockingRequestMissionControlClient();
        await using var server = await FingerServerHarness.StartAsync(
            missionControl,
            maxConcurrentConnections: 1);

        string first = await ReadFingerResponseAsync(server.Port, "kyle\r\n");
        await missionControl.WaitForStartedCountAsync(1, TimeSpan.FromSeconds(2));

        string second = await ReadFingerResponseAsync(server.Port, "joke\r\n");
        await missionControl.WaitForStartedCountAsync(2, TimeSpan.FromSeconds(2));

        Assert.Contains("Kyle content", first);
        Assert.Contains("Joke content", second);
        Assert.Equal(0, missionControl.FinishedCount);

        missionControl.Release();
    }

    [Fact]
    public async Task RequestTelemetryIsCancelledByIndependentTimeout()
    {
        var missionControl = new BlockingRequestMissionControlClient();
        await using var server = await FingerServerHarness.StartAsync(
            missionControl,
            maxConcurrentConnections: 1);

        string response = await ReadFingerResponseAsync(server.Port, "kyle\r\n");
        await missionControl.WaitForFinishedCountAsync(1, TimeSpan.FromSeconds(5));

        Assert.Contains("Kyle content", response);
        Assert.Equal(1, missionControl.CanceledCount);
    }

    [Fact]
    public async Task RequestTelemetryExceptionDoesNotPreventLaterRequests()
    {
        var missionControl = new ThrowingRequestMissionControlClient();
        await using var server = await FingerServerHarness.StartAsync(missionControl);

        string first = await ReadFingerResponseAsync(server.Port, "kyle\r\n");
        string second = await ReadFingerResponseAsync(server.Port, "joke\r\n");

        Assert.Contains("Kyle content", first);
        Assert.Contains("Joke content", second);
        Assert.Equal(2, missionControl.RequestAttempts);
    }

    [Fact]
    public async Task StartupTelemetryTimeoutDoesNotPreventAcceptingConnections()
    {
        var missionControl = new BlockingServiceStartedMissionControlClient();
        await using var server = await FingerServerHarness.StartAsync(missionControl);

        string response = await ReadFingerResponseAsync(server.Port, "kyle\r\n", TimeSpan.FromSeconds(5));

        Assert.Contains("Kyle content", response);
        Assert.Equal(1, missionControl.CanceledCount);
    }

    [Fact]
    public async Task ShutdownCompletesWithBlockedRequestTelemetry()
    {
        var missionControl = new BlockingRequestMissionControlClient();
        await using var server = await FingerServerHarness.StartAsync(missionControl);

        string response = await ReadFingerResponseAsync(server.Port, "kyle\r\n");
        await missionControl.WaitForStartedCountAsync(1, TimeSpan.FromSeconds(2));

        await server.StopAsync(TimeSpan.FromSeconds(2));

        Assert.Contains("Kyle content", response);
        Assert.True(server.Stopped);
    }

    [Fact]
    public async Task ConnectionSlotIsNotReleasedUntilProtocolProcessingCompletes()
    {
        var resolver = new BlockingResponseResolver();
        await using var server = await FingerServerHarness.StartAsync(
            new TestMissionControlClient(),
            maxConcurrentConnections: 1,
            responseResolver: resolver);

        using var firstClient = await ConnectAndSendAsync(server.Port, "kyle\r\n");
        await resolver.WaitForStartedCountAsync(1, TimeSpan.FromSeconds(2));

        Task<string> secondResponse = ReadFingerResponseAsync(server.Port, "joke\r\n");
        await Task.Delay(250);
        Assert.False(secondResponse.IsCompleted);

        resolver.Release();

        string first = await ReadRemainingAsync(firstClient, TimeSpan.FromSeconds(2));
        string second = await secondResponse.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("blocked response", first);
        Assert.Equal("blocked response", second);
    }

    private static FingerRequestCompletedEvent AssertTelemetryPayload(
        FingerWorker.FingerRequestTelemetryResult? telemetry)
    {
        Assert.NotNull(telemetry);
        return new FingerRequestCompletedEvent(
            telemetry.RequestReceived,
            telemetry.Request,
            telemetry.RequestLength,
            telemetry.Remote,
            telemetry.ResponseType,
            telemetry.DurationMilliseconds,
            telemetry.Outcome,
            telemetry.Succeeded);
    }

    private static string SerializePayload(FingerRequestCompletedEvent payload) =>
        JsonSerializer.Serialize(
            payload,
            FingerJsonContext.Default.FingerRequestCompletedEvent);

    private static FingerWorker CreateWorker(
        TestMissionControlClient missionControlClient,
        HappyFingerOptions? options = null,
        IFingerResponseResolver? responseResolver = null) =>
        new(
            NullLogger<FingerWorker>.Instance,
            missionControlClient,
            responseResolver ?? CreateResolver(),
            Options.Create(
                options ?? new HappyFingerOptions
                {
                    RequestTimeoutSeconds = 1
                }));

    private static IFingerResponseResolver CreateResolver(
        PlanFileResult? result = null,
        RandomSteamGameResult? randomSteamGameResult = null) =>
        new FingerResponseResolver(
            new TestPlanFileReader(
                result ??
                new PlanFileResult(
                    Available: false,
                    Content: "",
                    Truncated: false)),
            new TestRandomSteamGameClient(
                randomSteamGameResult ??
                new RandomSteamGameResult(
                    Succeeded: false,
                    Game: null)),
            new TestContentProvider());

    private static IPEndPoint CreateRemote() =>
        new(IPAddress.Parse("203.0.113.10"), 54321);

    private static async Task<string> ReadFingerResponseAsync(
        int port,
        string request,
        TimeSpan? timeout = null)
    {
        using TcpClient client = await ConnectAndSendAsync(port, request);
        return await ReadRemainingAsync(
            client,
            timeout ?? TimeSpan.FromSeconds(2));
    }

    private static async Task<TcpClient> ConnectAndSendAsync(
        int port,
        string request)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(
            TimeSpan.FromSeconds(2));
        await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes(request)).AsTask().WaitAsync(
            TimeSpan.FromSeconds(2));
        return client;
    }

    private static async Task<string> ReadRemainingAsync(
        TcpClient client,
        TimeSpan timeout)
    {
        await using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync().WaitAsync(timeout);
    }

    private sealed class FingerServerHarness : IAsyncDisposable
    {
        private readonly FingerWorker _worker;

        private FingerServerHarness(FingerWorker worker)
        {
            _worker = worker;
        }

        public int Port => _worker.BoundPort;
        public bool Stopped { get; private set; }

        public static async Task<FingerServerHarness> StartAsync(
            IMissionControlClient missionControlClient,
            int maxConcurrentConnections = 4,
            IFingerResponseResolver? responseResolver = null)
        {
            var worker = new FingerWorker(
                NullLogger<FingerWorker>.Instance,
                missionControlClient,
                responseResolver ?? CreateResolver(),
                Options.Create(new HappyFingerOptions
                {
                    ListenAddress = "127.0.0.1",
                    Port = 0,
                    MaxConcurrentConnections = maxConcurrentConnections,
                    RequestTimeoutSeconds = 1
                }));

            var harness = new FingerServerHarness(worker);
            await worker.StartAsync(CancellationToken.None);
            await harness.WaitForPortAsync();
            return harness;
        }

        private async Task WaitForPortAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (Port == 0)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(10, timeout.Token);
            }
        }

        public async Task StopAsync(TimeSpan? timeout = null)
        {
            if (Stopped)
            {
                return;
            }

            using var stopTimeout = new CancellationTokenSource(
                timeout ?? TimeSpan.FromSeconds(5));
            await _worker.StopAsync(stopTimeout.Token);
            Stopped = true;
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }
    }

    private sealed class TestMissionControlClient : IMissionControlClient
    {
        public List<PublishedEvent> Events { get; } = [];
        public bool ThrowOnPublish { get; init; }

        public Task<bool> TryPublishAsync<TPayload>(
            string eventType,
            TPayload payload,
            JsonTypeInfo<TPayload> payloadTypeInfo,
            DateTimeOffset occurredAt,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnPublish)
            {
                throw new InvalidOperationException("Simulated telemetry failure.");
            }

            Events.Add(
                new PublishedEvent(
                    eventType,
                    payload,
                    occurredAt,
                    correlationId));

            return Task.FromResult(true);
        }
    }

    private sealed record PublishedEvent(
        string EventType,
        object? Payload,
        DateTimeOffset OccurredAt,
        string? CorrelationId);

    private sealed class BlockingRequestMissionControlClient : IMissionControlClient
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _startedSignal = new(0);
        private readonly SemaphoreSlim _finishedSignal = new(0);
        private int _startedCount;
        private int _finishedCount;
        private int _canceledCount;

        public int FinishedCount => Volatile.Read(ref _finishedCount);
        public int CanceledCount => Volatile.Read(ref _canceledCount);

        public async Task<bool> TryPublishAsync<TPayload>(
            string eventType,
            TPayload payload,
            JsonTypeInfo<TPayload> payloadTypeInfo,
            DateTimeOffset occurredAt,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            if (eventType != FingerRequestCompletedEvent.EventName)
            {
                return true;
            }

            Interlocked.Increment(ref _startedCount);
            _startedSignal.Release();

            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _canceledCount);
                throw;
            }
            finally
            {
                Interlocked.Increment(ref _finishedCount);
                _finishedSignal.Release();
            }

            return true;
        }

        public void Release() =>
            _release.TrySetResult();

        public async Task WaitForStartedCountAsync(
            int expectedCount,
            TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (Volatile.Read(ref _startedCount) < expectedCount)
            {
                await _startedSignal.WaitAsync(cancellation.Token);
            }
        }

        public async Task WaitForFinishedCountAsync(
            int expectedCount,
            TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (FinishedCount < expectedCount)
            {
                await _finishedSignal.WaitAsync(cancellation.Token);
            }
        }
    }

    private sealed class BlockingServiceStartedMissionControlClient : IMissionControlClient
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _canceledCount;

        public int CanceledCount => Volatile.Read(ref _canceledCount);

        public async Task<bool> TryPublishAsync<TPayload>(
            string eventType,
            TPayload payload,
            JsonTypeInfo<TPayload> payloadTypeInfo,
            DateTimeOffset occurredAt,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            if (eventType != FingerServiceStartedEvent.EventName)
            {
                return true;
            }

            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _canceledCount);
                throw;
            }

            return true;
        }
    }

    private sealed class ThrowingRequestMissionControlClient : IMissionControlClient
    {
        private int _requestAttempts;

        public int RequestAttempts => Volatile.Read(ref _requestAttempts);

        public Task<bool> TryPublishAsync<TPayload>(
            string eventType,
            TPayload payload,
            JsonTypeInfo<TPayload> payloadTypeInfo,
            DateTimeOffset occurredAt,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            if (eventType == FingerRequestCompletedEvent.EventName)
            {
                Interlocked.Increment(ref _requestAttempts);
                throw new InvalidOperationException("Telemetry failure");
            }

            return Task.FromResult(true);
        }
    }

    private sealed class TestPlanFileReader(
        PlanFileResult result) : IPlanFileReader
    {
        public Task<PlanFileResult> ReadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class TestRandomSteamGameClient(
        RandomSteamGameResult result) : IRandomSteamGameClient
    {
        public Task<RandomSteamGameResult> GetRandomGameAsync(
            long steamId,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class RecordingResponseResolver : IFingerResponseResolver
    {
        public string? Request { get; private set; }

        public Task<FingerResponse> ResolveAsync(
            string? request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(
                new FingerResponse(
                    Encoding.UTF8.GetBytes("resolver response"),
                    FingerResponseTypes.Kyle));
        }
    }

    private sealed class BlockingResponseResolver : IFingerResponseResolver
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _startedSignal = new(0);
        private int _startedCount;

        public async Task<FingerResponse> ResolveAsync(
            string? request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _startedCount);
            _startedSignal.Release();

            await _release.Task.WaitAsync(cancellationToken);

            return new FingerResponse(
                Encoding.UTF8.GetBytes("blocked response"),
                FingerResponseTypes.Kyle);
        }

        public void Release() =>
            _release.TrySetResult();

        public async Task WaitForStartedCountAsync(
            int expectedCount,
            TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (Volatile.Read(ref _startedCount) < expectedCount)
            {
                await _startedSignal.WaitAsync(cancellation.Token);
            }
        }
    }

    private sealed class TestContentProvider : IFingerContentProvider
    {
        public Task<FingerContentResult> GetAsync(
            FingerContentKey key,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new FingerContentResult(
                    Available: true,
                    Content: $"{key} content",
                    UsedOverride: false,
                    Truncated: false));
    }

    private sealed class ScriptedStream(
        byte[] readBytes,
        IOException? throwOnWrite = null) : Stream
    {
        private readonly MemoryStream _written = new();
        private bool _hasRead;

        public byte[] WrittenBytes => _written.ToArray();
        public int FlushCount { get; private set; }
        public int WriteCount { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            FlushCount++;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_hasRead)
            {
                return ValueTask.FromResult(0);
            }

            _hasRead = true;
            int count = Math.Min(buffer.Length, readBytes.Length);
            readBytes.AsMemory(0, count).CopyTo(buffer);

            return ValueTask.FromResult(count);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (throwOnWrite is not null)
            {
                throw throwOnWrite;
            }

            WriteCount++;
            _written.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingReadStream(Exception exception) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
