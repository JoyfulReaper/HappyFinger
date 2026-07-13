using System.Text;

namespace HappyFinger.Tests;

public sealed class FingerResponseResolverTests
{
    public static TheoryData<string> NowQueries => new()
    {
        { "now" },
        { "NOW" },
        { "/W now" },
        { "/W\tnow" }
    };

    [Theory]
    [MemberData(nameof(NowQueries))]
    public async Task ResolveAsync_ReturnsFileBackedNowResponseWhenPlanIsAvailable(
        string request)
    {
        var reader = new TestPlanFileReader(
            new PlanFileResult(
                Available: true,
                Content: "Building HappyFinger into a useful public directory.",
                Truncated: false));
        var resolver = new FingerResponseResolver(reader);

        FingerResponse response =
            await resolver.ResolveAsync(request, CancellationToken.None);

        string content = Encoding.UTF8.GetString(response.Bytes.Span);
        Assert.Equal(FingerResponseTypes.Now, response.Type);
        Assert.Contains("Kyle's Plan", content);
        Assert.Contains(
            "Building HappyFinger into a useful public directory.",
            content);
    }

    [Fact]
    public async Task ResolveAsync_AppendsTruncationMarkerWhenPlanIsTruncated()
    {
        var resolver = new FingerResponseResolver(
            new TestPlanFileReader(
                new PlanFileResult(
                    Available: true,
                    Content: "short plan",
                    Truncated: true)));

        FingerResponse response =
            await resolver.ResolveAsync("now", CancellationToken.None);

        string content = Encoding.UTF8.GetString(response.Bytes.Span);
        Assert.Equal(FingerResponseTypes.Now, response.Type);
        Assert.Contains("short plan", content);
        Assert.Contains("[Plan truncated]", content);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsStaticNowResponseWhenPlanIsUnavailable()
    {
        var resolver = new FingerResponseResolver(
            new TestPlanFileReader(
                new PlanFileResult(
                    Available: false,
                    Content: "",
                    Truncated: false)));

        FingerResponse response =
            await resolver.ResolveAsync("now", CancellationToken.None);

        string content = Encoding.UTF8.GetString(response.Bytes.Span);
        Assert.Equal(FingerResponseTypes.Now, response.Type);
        Assert.Contains("Kyle's Current Plan", content);
        Assert.DoesNotContain("Kyle's Plan\r\n\r\n", content);
    }

    [Fact]
    public async Task ResolveAsync_ReusesStaticResponsesForNonNowRoutes()
    {
        var reader = new TestPlanFileReader(
            new PlanFileResult(
                Available: true,
                Content: "not used",
                Truncated: false));
        var resolver = new FingerResponseResolver(reader);

        FingerResponse response =
            await resolver.ResolveAsync("kyle", CancellationToken.None);

        Assert.Equal(FingerResponseTypes.Kyle, response.Type);
        Assert.Equal(0, reader.ReadCount);
    }

    private sealed class TestPlanFileReader(
        PlanFileResult result) : IPlanFileReader
    {
        public int ReadCount { get; private set; }

        public Task<PlanFileResult> ReadAsync(
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(result);
        }
    }
}
