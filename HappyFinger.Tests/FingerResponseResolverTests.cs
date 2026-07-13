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
        var randomSteamGameClient = new TestRandomSteamGameClient();
        var resolver = new FingerResponseResolver(
            reader,
            randomSteamGameClient);

        FingerResponse response =
            await resolver.ResolveAsync(request, CancellationToken.None);

        string content = Encoding.UTF8.GetString(response.Bytes.Span);
        Assert.Equal(FingerResponseTypes.Now, response.Type);
        Assert.Contains("Kyle's Plan", content);
        Assert.Contains(
            "Building HappyFinger into a useful public directory.",
            content);
        Assert.Equal(0, randomSteamGameClient.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_AppendsTruncationMarkerWhenPlanIsTruncated()
    {
        var resolver = new FingerResponseResolver(
            new TestPlanFileReader(
                new PlanFileResult(
                    Available: true,
                    Content: "short plan",
                    Truncated: true)),
            new TestRandomSteamGameClient());

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
                    Truncated: false)),
            new TestRandomSteamGameClient());

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
        var randomSteamGameClient = new TestRandomSteamGameClient();
        var resolver = new FingerResponseResolver(
            reader,
            randomSteamGameClient);

        FingerResponse response =
            await resolver.ResolveAsync("kyle", CancellationToken.None);

        Assert.Equal(FingerResponseTypes.Kyle, response.Type);
        Assert.Equal(0, reader.ReadCount);
        Assert.Equal(0, randomSteamGameClient.CallCount);
    }

    public static TheoryData<string> SteamIdQueries => new()
    {
        { "76561198000000000" },
        { "/W 76561198000000000" },
        { "/W\t76561198000000000" }
    };

    [Theory]
    [MemberData(nameof(SteamIdQueries))]
    public async Task ResolveAsync_ValidSteamIdCallsRandomSteamGameClient(
        string request)
    {
        var randomSteamGameClient = new TestRandomSteamGameClient(
            new RandomSteamGameResult(
                Succeeded: true,
                Game: new RandomGameDetails
                {
                    Id = 220,
                    Name = "Half-Life\u001b[31m 2",
                    PlaytimeForever = 872,
                    Playtime2Weeks = 192,
                    RTimeLastPlayed = 1762204440
                }));
        var resolver = new FingerResponseResolver(
            new TestPlanFileReader(UnavailablePlan),
            randomSteamGameClient);

        FingerResponse response =
            await resolver.ResolveAsync(request, CancellationToken.None);

        string content = Encoding.UTF8.GetString(response.Bytes.Span);
        Assert.Equal(FingerResponseTypes.RandomGame, response.Type);
        Assert.Equal(1, randomSteamGameClient.CallCount);
        Assert.Equal(76561198000000000, randomSteamGameClient.SteamIds.Single());
        Assert.Contains("Half-Life 2", content);
        Assert.Contains("Steam App:   220", content);
        Assert.Contains("Playtime:    14h 32m", content);
        Assert.Contains("Last two weeks: 3h 12m", content);
        Assert.Contains("Last played: 2025-11-03 21:14 UTC", content);
        Assert.Contains("https://store.steampowered.com/app/220", content);
        Assert.Contains("\r\n", content);
        Assert.DoesNotContain(content, character => character == (char)0x1b);
        Assert.DoesNotContain("\n", content.Replace("\r\n", string.Empty));
    }

    [Fact]
    public async Task ResolveAsync_RandomSteamStaticRouteDoesNotCallRandomSteamGameClient()
    {
        var randomSteamGameClient = new TestRandomSteamGameClient();
        var resolver = new FingerResponseResolver(
            new TestPlanFileReader(UnavailablePlan),
            randomSteamGameClient);

        FingerResponse response =
            await resolver.ResolveAsync("randomsteam", CancellationToken.None);

        Assert.Equal(FingerResponseTypes.RandomSteam, response.Type);
        Assert.Equal(0, randomSteamGameClient.CallCount);
    }

    public static TheoryData<string> InvalidNumericQueries => new()
    {
        { "123" },
        { "7656119800000000" },
        { "7656119800000000x" }
    };

    [Theory]
    [MemberData(nameof(InvalidNumericQueries))]
    public async Task ResolveAsync_InvalidNumericQueriesReturnNotFoundWithoutHttp(
        string request)
    {
        var randomSteamGameClient = new TestRandomSteamGameClient();
        var resolver = new FingerResponseResolver(
            new TestPlanFileReader(UnavailablePlan),
            randomSteamGameClient);

        FingerResponse response =
            await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.Equal(FingerResponseTypes.NotFound, response.Type);
        Assert.Equal(0, randomSteamGameClient.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_ForwardingQueryDoesNotExtractSteamId()
    {
        var randomSteamGameClient = new TestRandomSteamGameClient();
        var resolver = new FingerResponseResolver(
            new TestPlanFileReader(UnavailablePlan),
            randomSteamGameClient);

        FingerResponse response =
            await resolver.ResolveAsync(
                "76561198000000000@example.com",
                CancellationToken.None);

        Assert.Equal(FingerResponseTypes.ForwardingNotSupported, response.Type);
        Assert.Equal(0, randomSteamGameClient.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_UnavailableRandomSteamGameReturnsGenericMessage()
    {
        var resolver = new FingerResponseResolver(
            new TestPlanFileReader(UnavailablePlan),
            new TestRandomSteamGameClient(
                new RandomSteamGameResult(
                    Succeeded: false,
                    Game: null)));

        FingerResponse response =
            await resolver.ResolveAsync("76561198000000000", CancellationToken.None);

        string content = Encoding.UTF8.GetString(response.Bytes.Span);
        Assert.Equal(FingerResponseTypes.RandomGameUnavailable, response.Type);
        Assert.Contains("A random game could not be selected.", content);
        Assert.DoesNotContain("76561198000000000", content);
    }

    private static readonly PlanFileResult UnavailablePlan =
        new(
            Available: false,
            Content: "",
            Truncated: false);

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

    private sealed class TestRandomSteamGameClient(
        RandomSteamGameResult? result = null) : IRandomSteamGameClient
    {
        private readonly RandomSteamGameResult _result =
            result ??
            new RandomSteamGameResult(
                Succeeded: false,
                Game: null);

        public int CallCount { get; private set; }
        public List<long> SteamIds { get; } = [];

        public Task<RandomSteamGameResult> GetRandomGameAsync(
            long steamId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            SteamIds.Add(steamId);
            return Task.FromResult(_result);
        }
    }
}
