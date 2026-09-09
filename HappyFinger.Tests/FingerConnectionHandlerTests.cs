using HappyFinger.Events;
using HappyFinger.Finger;
using HappyFinger.Plan;
using HappyFinger.Steam;
using JoyfulReaperLib.MissionControl;
using JoyfulReaperLib.TcpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization.Metadata;

namespace HappyFinger.Tests;

public sealed class FingerConnectionHandlerTests
{
    private static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(2);

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
        Assert.Equal(
            expected,
            FingerConnectionHandler.SanitizeTelemetryRequest(request));

    [Theory]
    [MemberData(nameof(SuccessfulRequests))]
    public async Task ProcessAsync_ReturnsSelectedResponseTypeForSuccessfulRequest(
        string request,
        string expectedResponseType)
    {
        IFingerResponseResolver responseResolver = CreateResolver(
                new PlanFileResult(
                    Available: true,
                    Content: "public plan content",
                    Truncated: false));
        var stream = new ScriptedStream(Encoding.UTF8.GetBytes(request));

        FingerSessionResult result = AssertSessionResult(
            await ProcessAsync(
            stream,
            responseResolver: responseResolver));

        Assert.Equal(expectedResponseType, result.ResponseType);
        Assert.Equal("served", result.Outcome);
        Assert.True(result.Succeeded);
        Assert.Equal(1, stream.WriteCount);
        Assert.Equal(1, stream.FlushCount);
    }

    [Fact]
    public async Task ProcessAsync_FileBackedNowResultDoesNotIncludePlanContentOrPath()
    {
        IFingerResponseResolver responseResolver = CreateResolver(
                new PlanFileResult(
                    Available: true,
                    Content: "private plan detail",
                    Truncated: false));
        var stream = new ScriptedStream(Encoding.UTF8.GetBytes("now\r\n"));

        FingerSessionResult result = AssertSessionResult(
            await ProcessAsync(
            stream,
            responseResolver: responseResolver));

        string resultText = result.ToString();

        Assert.Equal(FingerResponseTypes.Now, result.ResponseType);
        Assert.Equal("served", result.Outcome);
        Assert.True(result.Succeeded);
        Assert.DoesNotContain("private plan detail", resultText);
        Assert.DoesNotContain("data/.plan", resultText);
    }

    [Fact]
    public async Task ProcessAsync_RandomGameReturnsOnlyControlledTelemetry()
    {
        IFingerResponseResolver responseResolver = CreateResolver(
                randomSteamGameResult: new RandomSteamGameResult(
                    Succeeded: true,
                    Game: new RandomGameDetails
                    {
                        Id = 42424242,
                        Name = "Half-Life 2",
                        PlaytimeForever = 872,
                        RTimeLastPlayed = 1762204440
                    }));
        var stream = new ScriptedStream(
            Encoding.UTF8.GetBytes("76561198000000000\r\n"));

        FingerSessionResult result = AssertSessionResult(
            await ProcessAsync(
            stream,
            responseResolver: responseResolver));

        string resultText = result.ToString();

        Assert.Equal(FingerResponseTypes.RandomGame, result.ResponseType);
        Assert.Equal("76561198000000000", result.Request);
        Assert.Equal(17, result.RequestLength);
        Assert.Equal("served", result.Outcome);
        Assert.True(result.Succeeded);
        Assert.DoesNotContain("Half-Life", resultText);
        Assert.DoesNotContain("42424242", resultText);
        Assert.DoesNotContain("872", resultText);
        Assert.DoesNotContain("1762204440", resultText);
        Assert.DoesNotContain("randomsteam.kgivler.com", resultText);
    }

    [Fact]
    public async Task ProcessAsync_SanitizesTelemetryRequestWithoutChangingProtocolRequest()
    {
        var responseResolver = new RecordingResponseResolver();
        var stream = new ScriptedStream(
            Encoding.UTF8.GetBytes("  ky\tle\u0001\r\n"));

        FingerSessionResult result = AssertSessionResult(
            await ProcessAsync(
            stream,
            responseResolver: responseResolver));

        Assert.Equal("ky le", result.Request);
        Assert.Equal("  ky\tle\u0001\r\n", responseResolver.Request);
        Assert.Equal("resolver response", Encoding.UTF8.GetString(stream.WrittenBytes));
    }

    [Fact]
    public async Task ProcessAsync_RandomGameUnavailableReturnsServedResult()
    {
        IFingerResponseResolver responseResolver = CreateResolver(
                randomSteamGameResult: new RandomSteamGameResult(
                    Succeeded: false,
                    Game: null));
        var stream = new ScriptedStream(
            Encoding.UTF8.GetBytes("76561198000000000\r\n"));

        FingerSessionResult result = AssertSessionResult(
            await ProcessAsync(
            stream,
            responseResolver: responseResolver));

        Assert.Equal(FingerResponseTypes.RandomGameUnavailable, result.ResponseType);
        Assert.Equal("served", result.Outcome);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ProcessAsync_TimeoutBeforeRoutingReportsNone()
    {
        var stream = new ThrowingReadStream(new OperationCanceledException());

        FingerSessionResult result =
            AssertSessionResult(await ProcessAsync(stream));

        Assert.Equal(FingerResponseTypes.None, result.ResponseType);
        Assert.Equal("timeout", result.Outcome);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ProcessAsync_MalformedRequestBeforeRoutingReportsNone()
    {
        var stream = new ScriptedStream(Encoding.UTF8.GetBytes(new string('x', 1024)));

        FingerSessionResult result =
            AssertSessionResult(await ProcessAsync(stream));

        Assert.Equal(FingerResponseTypes.None, result.ResponseType);
        Assert.Equal("malformed", result.Outcome);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ProcessAsync_WriteFailureAfterRoutingKeepsSelectedResponseType()
    {
        var stream = new ScriptedStream(
            Encoding.UTF8.GetBytes("\r\n"),
            throwOnWrite: new IOException("Simulated write failure."));

        FingerSessionResult result =
            AssertSessionResult(await ProcessAsync(stream));

        Assert.Equal(FingerResponseTypes.Directory, result.ResponseType);
        Assert.Equal("io-error", result.Outcome);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ProcessAsync_IgnoredTelemetrySourceReturnsNoResult()
    {
        var options = new HappyFingerOptions
        {
            TelemetryIgnoredRemoteAddresses = ["203.0.113.10"],
            RequestTimeoutSeconds = 1
        };
        var stream = new ScriptedStream(Encoding.UTF8.GetBytes("kyle\r\n"));

        FingerSessionResult? result = await ProcessAsync(
            stream,
            options: options);

        Assert.Null(result);
        Assert.Contains(
            "Kyle content",
            Encoding.UTF8.GetString(stream.WrittenBytes));
    }

    [Fact]
    public void IsIgnoredTelemetrySource_EmptyListDoesNotIgnoreClient()
    {
        bool isIgnored = FingerConnectionHandler.IsIgnoredTelemetrySource(
            CreateRemote(),
            []);

        Assert.False(isIgnored);
    }

    [Fact]
    public void IsIgnoredTelemetrySource_OneMatchingAddressIgnoresClient()
    {
        bool isIgnored = FingerConnectionHandler.IsIgnoredTelemetrySource(
            CreateRemote(),
            ["203.0.113.10"]);

        Assert.True(isIgnored);
    }

    [Fact]
    public void IsIgnoredTelemetrySource_MatchingAddressAmongMultipleIgnoresClient()
    {
        bool isIgnored = FingerConnectionHandler.IsIgnoredTelemetrySource(
            CreateRemote(),
            ["192.0.2.1", "203.0.113.10", "198.51.100.1"]);

        Assert.True(isIgnored);
    }

    [Fact]
    public void IsIgnoredTelemetrySource_NonMatchingAddressDoesNotIgnoreClient()
    {
        bool isIgnored = FingerConnectionHandler.IsIgnoredTelemetrySource(
            CreateRemote(),
            ["192.0.2.1", "198.51.100.1"]);

        Assert.False(isIgnored);
    }

    [Fact]
    public void IsIgnoredTelemetrySource_InvalidAddressDoesNotThrowOrIgnoreClient()
    {
        bool isIgnored = FingerConnectionHandler.IsIgnoredTelemetrySource(
            CreateRemote(),
            ["not-an-ip-address"]);

        Assert.False(isIgnored);
    }

    [Theory]
    [InlineData("203.0.113.10", "::ffff:203.0.113.10")]
    [InlineData("::ffff:203.0.113.10", "203.0.113.10")]
    public void IsIgnoredTelemetrySource_NormalizesIpv4AndIpv4MappedIpv6(
        string remoteAddress,
        string configuredAddress)
    {
        var remote = new IPEndPoint(IPAddress.Parse(remoteAddress), 54321);

        bool isIgnored = FingerConnectionHandler.IsIgnoredTelemetrySource(
            remote,
            [configuredAddress]);

        Assert.True(isIgnored);
    }

    [Fact]
    public async Task ProcessAsync_ShutdownCancellationReturnsNoResult()
    {
        var stream = new ThrowingReadStream(new OperationCanceledException());
        using var stoppingTokenSource = new CancellationTokenSource();
        await stoppingTokenSource.CancelAsync();

        FingerSessionResult? result = await ProcessAsync(
            stream,
            cancellationToken: stoppingTokenSource.Token);

        Assert.Null(result);
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
        var stopwatch = Stopwatch.StartNew();
        await using var server = await FingerServerHarness.StartAsync(missionControl);
        stopwatch.Stop();

        string response = await ReadFingerResponseAsync(server.Port, "kyle\r\n", TimeSpan.FromSeconds(5));

        Assert.Contains("Kyle content", response);
        Assert.Equal(1, missionControl.AttemptCount);
        Assert.Equal(1, missionControl.CanceledCount);
        Assert.InRange(
            stopwatch.Elapsed,
            TimeSpan.FromSeconds(1.5),
            HostTimeout);
    }

    [Fact]
    public async Task ShutdownCompletesWithBlockedRequestTelemetry()
    {
        var missionControl = new BlockingRequestMissionControlClient();
        await using var server = await FingerServerHarness.StartAsync(missionControl);

        string response = await ReadFingerResponseAsync(server.Port, "kyle\r\n");
        await missionControl.WaitForStartedCountAsync(1, TimeSpan.FromSeconds(2));

        await server.StopAsync(HostTimeout);

        Assert.Contains("Kyle content", response);
        Assert.True(server.Stopped);
    }

    [Fact]
    public async Task SharedHostWaitsForConnectionSlotUntilProtocolProcessingCompletes()
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

    [Fact]
    public async Task SharedHostPublishesOneStartupEventForConfiguredEndpoint()
    {
        var missionControl = new TestMissionControlClient();

        await using var server =
            await FingerServerHarness.StartAsync(missionControl);

        PublishedEvent published = await missionControl.WaitForEventAsync(
            FingerServiceStartedEvent.EventName,
            ShortTimeout);

        FingerServiceStartedEvent payload =
            Assert.IsType<FingerServiceStartedEvent>(published.Payload);

        Assert.Equal($"127.0.0.1:{server.Port}", payload.ListenAddress);
        Assert.Single(
            missionControl.Events,
            item => item.EventType == FingerServiceStartedEvent.EventName);
        Assert.Null(published.CorrelationId);
    }

    [Fact]
    public async Task SharedHostPublishesExpectedRequestCompletedTelemetry()
    {
        var missionControl = new TestMissionControlClient();
        DateTimeOffset beforeRequest = DateTimeOffset.UtcNow;

        await using var server =
            await FingerServerHarness.StartAsync(missionControl);

        string response =
            await ReadFingerResponseAsync(server.Port, "kyle\r\n");
        PublishedEvent published = await missionControl.WaitForEventAsync(
            FingerRequestCompletedEvent.EventName,
            ShortTimeout);

        FingerRequestCompletedEvent payload =
            Assert.IsType<FingerRequestCompletedEvent>(published.Payload);

        Assert.Contains("Kyle content", response);
        Assert.Equal(FingerRequestCompletedEvent.EventName, published.EventType);
        Assert.Equal("kyle", payload.Request);
        Assert.Equal(4, payload.RequestLength);
        Assert.Equal(FingerResponseTypes.Kyle, payload.ResponseType);
        Assert.Equal("served", payload.Outcome);
        Assert.True(payload.RequestReceived);
        Assert.True(payload.Succeeded);
        Assert.True(payload.DurationMilliseconds >= 0);
        Assert.InRange(
            published.OccurredAt,
            beforeRequest,
            DateTimeOffset.UtcNow);
        Assert.False(string.IsNullOrWhiteSpace(published.CorrelationId));
        Assert.Equal(32, published.CorrelationId.Length);
    }

    [Fact]
    public async Task IgnoredTelemetrySourceReceivesResponseWithoutRequestEvent()
    {
        var missionControl = new TestMissionControlClient();
        await using var server = await FingerServerHarness.StartAsync(
            missionControl,
            telemetryIgnoredRemoteAddresses: ["127.0.0.1"]);

        string response =
            await ReadFingerResponseAsync(server.Port, "kyle\r\n");
        await Task.Delay(250);

        Assert.Contains("Kyle content", response);
        Assert.DoesNotContain(
            missionControl.Events,
            item => item.EventType == FingerRequestCompletedEvent.EventName);
    }

    private static FingerSessionResult AssertSessionResult(
        FingerSessionResult? result)
    {
        Assert.NotNull(result);
        return result;
    }

    private static Task<FingerSessionResult?> ProcessAsync(
        Stream stream,
        IFingerResponseResolver? responseResolver = null,
        HappyFingerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        FingerConnectionHandler.ProcessAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            responseResolver ?? CreateResolver(),
            options ?? new HappyFingerOptions
            {
                RequestTimeoutSeconds = 1
            },
            NullLogger<FingerConnectionHandler>.Instance,
            cancellationToken);

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

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class FingerServerHarness : IAsyncDisposable
    {
        private readonly IHost _host;

        private FingerServerHarness(IHost host, int port)
        {
            _host = host;
            Port = port;
        }

        public int Port { get; }
        public bool Stopped { get; private set; }

        public static async Task<FingerServerHarness> StartAsync(
            IMissionControlClient missionControlClient,
            int maxConcurrentConnections = 4,
            IFingerResponseResolver? responseResolver = null,
            string[]? telemetryIgnoredRemoteAddresses = null)
        {
            int port = GetAvailablePort();
            var options = new HappyFingerOptions
            {
                ListenAddress = "127.0.0.1",
                Port = port,
                MaxConcurrentConnections = maxConcurrentConnections,
                RequestTimeoutSeconds = 1,
                TelemetryIgnoredRemoteAddresses =
                    telemetryIgnoredRemoteAddresses ?? []
            };

            IHost host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices(services =>
                {
                    services.AddSingleton(missionControlClient);
                    services.AddSingleton(
                        responseResolver ?? CreateResolver());
                    services.AddSingleton<IOptions<HappyFingerOptions>>(
                        Options.Create(options));
                    services.AddTcpServer<
                        FingerConnectionHandler,
                        HappyFingerOptions>();
                    services.AddHostedService<FingerLifecycleService>();
                })
                .Build();

            var harness = new FingerServerHarness(host, port);
            using var startupTimeout =
                new CancellationTokenSource(HostTimeout);

            try
            {
                await host.StartAsync(startupTimeout.Token);
                return harness;
            }
            catch
            {
                host.Dispose();
                throw;
            }
        }

        public async Task StopAsync(TimeSpan? timeout = null)
        {
            if (Stopped)
            {
                return;
            }

            using var stopTimeout = new CancellationTokenSource(
                timeout ?? HostTimeout);
            await _host.StopAsync(stopTimeout.Token);
            Stopped = true;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopAsync();
            }
            finally
            {
                _host.Dispose();
            }
        }
    }

    private sealed class TestMissionControlClient : IMissionControlClient
    {
        private readonly ConcurrentQueue<PublishedEvent> _events = [];
        private readonly SemaphoreSlim _eventSignal = new(0);

        public IReadOnlyCollection<PublishedEvent> Events =>
            _events.ToArray();

        public Task<bool> TryPublishAsync<TPayload>(
            string eventType,
            TPayload payload,
            JsonTypeInfo<TPayload> payloadTypeInfo,
            DateTimeOffset occurredAt,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            _events.Enqueue(
                new PublishedEvent(
                    eventType,
                    payload,
                    occurredAt,
                    correlationId));
            _eventSignal.Release();

            return Task.FromResult(true);
        }

        public async Task<PublishedEvent> WaitForEventAsync(
            string eventType,
            TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);

            while (true)
            {
                PublishedEvent? published =
                    _events.FirstOrDefault(
                        item => item.EventType == eventType);

                if (published is not null)
                {
                    return published;
                }

                await _eventSignal.WaitAsync(cancellation.Token);
            }
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
        private int _attemptCount;
        private int _canceledCount;

        public int AttemptCount => Volatile.Read(ref _attemptCount);
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

            Interlocked.Increment(ref _attemptCount);

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
