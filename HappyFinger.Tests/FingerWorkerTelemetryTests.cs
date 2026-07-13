using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;

namespace HappyFinger.Tests;

public sealed class FingerWorkerTelemetryTests
{
    public static TheoryData<string, string> SuccessfulRequests => new()
    {
        { "\r\n", FingerResponseTypes.Directory },
        { "kyle\r\n", FingerResponseTypes.Kyle },
        { "unknown\r\n", FingerResponseTypes.NotFound },
        { "name@host\r\n", FingerResponseTypes.ForwardingNotSupported },
        { "joke\r\n", FingerResponseTypes.Joke }
    };

    [Theory]
    [MemberData(nameof(SuccessfulRequests))]
    public async Task HandleConnectionAsync_PublishesSelectedResponseTypeForSuccessfulRequest(
        string request,
        string expectedResponseType)
    {
        var missionControlClient = new TestMissionControlClient();
        FingerWorker worker = CreateWorker(missionControlClient);
        var stream = new ScriptedStream(Encoding.UTF8.GetBytes(request));

        await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None);

        PublishedEvent publishedEvent = Assert.Single(missionControlClient.Events);
        Assert.Equal("happyfinger.request.completed", publishedEvent.EventType);
        var payload = Assert.IsType<FingerRequestCompletedEvent>(publishedEvent.Payload);
        Assert.Equal(expectedResponseType, payload.ResponseType);
        Assert.Equal("served", payload.Outcome);
        Assert.True(payload.Succeeded);
    }

    [Fact]
    public async Task HandleConnectionAsync_TimeoutBeforeRoutingReportsNone()
    {
        var missionControlClient = new TestMissionControlClient();
        FingerWorker worker = CreateWorker(missionControlClient);
        var stream = new ThrowingReadStream(new OperationCanceledException());

        await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None);

        var payload = AssertPublishedPayload(missionControlClient);
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

        await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None);

        var payload = AssertPublishedPayload(missionControlClient);
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

        await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None);

        var payload = AssertPublishedPayload(missionControlClient);
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

        await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            CancellationToken.None);

        Assert.Contains(
            "Login: kyle",
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

        await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            new IPEndPoint(IPAddress.Parse("203.0.113.10"), 54321),
            CancellationToken.None);

        Assert.Empty(missionControlClient.Events);
    }

    [Fact]
    public async Task HandleConnectionAsync_ShutdownCancellationPublishesNoEvent()
    {
        var missionControlClient = new TestMissionControlClient();
        FingerWorker worker = CreateWorker(missionControlClient);
        var stream = new ThrowingReadStream(new OperationCanceledException());
        using var stoppingTokenSource = new CancellationTokenSource();
        await stoppingTokenSource.CancelAsync();

        await worker.HandleConnectionAsync(
            connectionId: 1,
            stream,
            CreateRemote(),
            stoppingTokenSource.Token);

        Assert.Empty(missionControlClient.Events);
    }

    private static FingerRequestCompletedEvent AssertPublishedPayload(
        TestMissionControlClient missionControlClient)
    {
        PublishedEvent publishedEvent = Assert.Single(missionControlClient.Events);
        Assert.Equal("happyfinger.request.completed", publishedEvent.EventType);
        return Assert.IsType<FingerRequestCompletedEvent>(publishedEvent.Payload);
    }

    private static FingerWorker CreateWorker(
        TestMissionControlClient missionControlClient,
        HappyFingerOptions? options = null) =>
        new(
            NullLogger<FingerWorker>.Instance,
            missionControlClient,
            Options.Create(
                options ?? new HappyFingerOptions
                {
                    RequestTimeoutSeconds = 1
                }));

    private static IPEndPoint CreateRemote() =>
        new(IPAddress.Parse("203.0.113.10"), 54321);

    private sealed class TestMissionControlClient : IMissionControlClient
    {
        public List<PublishedEvent> Events { get; } = [];
        public bool ThrowOnPublish { get; init; }

        public Task<bool> TryPublishAsync<TPayload>(
            string eventType,
            TPayload payload,
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

    private sealed class ScriptedStream(
        byte[] readBytes,
        IOException? throwOnWrite = null) : Stream
    {
        private readonly MemoryStream _written = new();
        private bool _hasRead;

        public byte[] WrittenBytes => _written.ToArray();
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

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

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
