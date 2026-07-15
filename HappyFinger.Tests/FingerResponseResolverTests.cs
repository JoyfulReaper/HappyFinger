using HappyFinger.Steam;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
            randomSteamGameClient,
            new TestContentProvider());

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
            new TestRandomSteamGameClient(),
            new TestContentProvider());

        FingerResponse response =
            await resolver.ResolveAsync("now", CancellationToken.None);

        string content = Encoding.UTF8.GetString(response.Bytes.Span);
        Assert.Equal(FingerResponseTypes.Now, response.Type);
        Assert.Contains("short plan", content);
        Assert.Contains("[Plan truncated]", content);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNowFallbackContentWhenPlanIsUnavailable()
    {
        var resolver = new FingerResponseResolver(
            new TestPlanFileReader(UnavailablePlan),
            new TestRandomSteamGameClient(),
            new TestContentProvider(
                new Dictionary<FingerContentKey, string>
                {
                    [FingerContentKey.NowFallback] = "fallback now"
                }));

        FingerResponse response =
            await resolver.ResolveAsync("now", CancellationToken.None);

        string content = Encoding.UTF8.GetString(response.Bytes.Span);
        Assert.Equal(FingerResponseTypes.Now, response.Type);
        Assert.Contains("fallback now", content);
        Assert.DoesNotContain("Kyle's Plan\r\n\r\n", content);
    }

    public static TheoryData<string?, string, string> StaticRoutes => new()
    {
        { "", FingerResponseTypes.Directory, "directory content" },
        { "   ", FingerResponseTypes.Directory, "directory content" },
        { "/W", FingerResponseTypes.Directory, "directory content" },
        { "/W kyle", FingerResponseTypes.Kyle, "kyle content" },
        { "/W\tkyle", FingerResponseTypes.Kyle, "kyle content" },
        { "/Wrong", FingerResponseTypes.NotFound, "not-found content" },
        { "/Whatever", FingerResponseTypes.NotFound, "not-found content" },
        { "/Wkyle", FingerResponseTypes.NotFound, "not-found content" },
        { "kyle", FingerResponseTypes.Kyle, "kyle content" },
        { "KYLE", FingerResponseTypes.Kyle, "kyle content" },
        { "projects", FingerResponseTypes.Projects, "projects content" },
        { "services", FingerResponseTypes.Services, "services content" },
        { "randomsteam", FingerResponseTypes.RandomSteam, "randomsteam content" },
        { "reapershell", FingerResponseTypes.ReaperShell, "reapershell content" },
        { "help", FingerResponseTypes.Help, "help content" },
        { "joke", FingerResponseTypes.Joke, "joke content" },
        { "user@example.com", FingerResponseTypes.ForwardingNotSupported, "forwarding-not-supported content" },
        { "unknown", FingerResponseTypes.NotFound, "not-found content" }
    };

    [Theory]
    [MemberData(nameof(StaticRoutes))]
    public async Task ResolveAsync_ReturnsExpectedFileBackedStaticRoute(
        string? request,
        string expectedType,
        string expectedContent)
    {
        var resolver = new FingerResponseResolver(
            new TestPlanFileReader(
                new PlanFileResult(
                    Available: true,
                    Content: "not used",
                    Truncated: false)),
            new TestRandomSteamGameClient(),
            new TestContentProvider());

        FingerResponse response =
            await resolver.ResolveAsync(request, CancellationToken.None);

        Assert.Equal(expectedType, response.Type);
        Assert.Contains(
            expectedContent,
            Encoding.UTF8.GetString(response.Bytes.Span));
    }

    [Fact]
    public async Task ResolveAsync_DoesNotReadPlanOrRandomSteamClientForStaticRoutes()
    {
        var reader = new TestPlanFileReader(
            new PlanFileResult(
                Available: true,
                Content: "not used",
                Truncated: false));
        var randomSteamGameClient = new TestRandomSteamGameClient();
        var resolver = new FingerResponseResolver(
            reader,
            randomSteamGameClient,
            new TestContentProvider());

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
            randomSteamGameClient,
            new TestContentProvider());

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
            randomSteamGameClient,
            new TestContentProvider());

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
            randomSteamGameClient,
            new TestContentProvider());

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
            randomSteamGameClient,
            new TestContentProvider());

        FingerResponse response =
            await resolver.ResolveAsync(
                "76561198000000000@example.com",
                CancellationToken.None);

        Assert.Equal(FingerResponseTypes.ForwardingNotSupported, response.Type);
        Assert.Equal(0, randomSteamGameClient.CallCount);
    }

    [Fact]
    public async Task ResolveAsync_UnavailableRandomSteamGameReturnsContentFile()
    {
        var resolver = new FingerResponseResolver(
            new TestPlanFileReader(UnavailablePlan),
            new TestRandomSteamGameClient(
                new RandomSteamGameResult(
                    Succeeded: false,
                    Game: null)),
            new TestContentProvider(
                new Dictionary<FingerContentKey, string>
                {
                    [FingerContentKey.RandomGameUnavailable] = "unavailable from file"
                }));

        FingerResponse response =
            await resolver.ResolveAsync("76561198000000000", CancellationToken.None);

        string content = Encoding.UTF8.GetString(response.Bytes.Span);
        Assert.Equal(FingerResponseTypes.RandomGameUnavailable, response.Type);
        Assert.Contains("unavailable from file", content);
        Assert.DoesNotContain("76561198000000000", content);
    }

    [Fact]
    public async Task ResolveAsync_UsesEmergencyContentWhenContentFileIsUnavailable()
    {
        var resolver = new FingerResponseResolver(
            new TestPlanFileReader(UnavailablePlan),
            new TestRandomSteamGameClient(),
            new TestContentProvider(new Dictionary<FingerContentKey, string>()));

        FingerResponse response =
            await resolver.ResolveAsync("help", CancellationToken.None);

        string content = Encoding.UTF8.GetString(response.Bytes.Span);
        Assert.Equal(FingerResponseTypes.Help, response.Type);
        Assert.Contains("HappyFinger content is temporarily unavailable.", content);
    }

    [Fact]
    public async Task ResolveAsync_ReadsEditedOverrideContentOnNextRequest()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "HappyFinger.Tests",
            Guid.NewGuid().ToString("N"));
        string packaged = Path.Combine(root, "packaged");
        string overrides = Path.Combine(root, "records");
        Directory.CreateDirectory(packaged);
        Directory.CreateDirectory(overrides);

        try
        {
            string fileName = FingerContentFileNames.GetFileName(FingerContentKey.Kyle);
            await File.WriteAllTextAsync(Path.Combine(packaged, fileName), "packaged");
            await File.WriteAllTextAsync(Path.Combine(overrides, fileName), "first");

            var provider = new FileFingerContentProvider(
                Options.Create(
                    new FingerContentOptions
                    {
                        OverrideDirectory = overrides,
                        MaxBytes = 16 * 1024
                    }),
                NullLogger<FileFingerContentProvider>.Instance,
                packaged);
            var resolver = new FingerResponseResolver(
                new TestPlanFileReader(UnavailablePlan),
                new TestRandomSteamGameClient(),
                provider);

            FingerResponse first =
                await resolver.ResolveAsync("kyle", CancellationToken.None);

            await File.WriteAllTextAsync(Path.Combine(overrides, fileName), "second");

            FingerResponse second =
                await resolver.ResolveAsync("kyle", CancellationToken.None);

            Assert.Contains("first", Encoding.UTF8.GetString(first.Bytes.Span));
            Assert.Contains("second", Encoding.UTF8.GetString(second.Bytes.Span));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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

    private sealed class TestContentProvider(
        IReadOnlyDictionary<FingerContentKey, string>? content = null) : IFingerContentProvider
    {
        private readonly IReadOnlyDictionary<FingerContentKey, string> _content =
            content ??
            new Dictionary<FingerContentKey, string>
            {
                [FingerContentKey.Directory] = "directory content",
                [FingerContentKey.Kyle] = "kyle content",
                [FingerContentKey.Projects] = "projects content",
                [FingerContentKey.Services] = "services content",
                [FingerContentKey.RandomSteam] = "randomsteam content",
                [FingerContentKey.ReaperShell] = "reapershell content",
                [FingerContentKey.Help] = "help content",
                [FingerContentKey.Joke] = "joke content",
                [FingerContentKey.NotFound] = "not-found content",
                [FingerContentKey.ForwardingNotSupported] = "forwarding-not-supported content",
                [FingerContentKey.NowFallback] = "now fallback content",
                [FingerContentKey.RandomGameUnavailable] = "random game unavailable content"
            };

        public Task<FingerContentResult> GetAsync(
            FingerContentKey key,
            CancellationToken cancellationToken)
        {
            if (!_content.TryGetValue(key, out string? value))
            {
                return Task.FromResult(
                    new FingerContentResult(
                        Available: false,
                        Content: "",
                        UsedOverride: false,
                        Truncated: false));
            }

            return Task.FromResult(
                new FingerContentResult(
                    Available: true,
                    Content: value,
                    UsedOverride: false,
                    Truncated: false));
        }
    }
}
