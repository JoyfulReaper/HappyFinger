using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace HappyFinger.Tests;

public sealed class RandomSteamGameClientTests
{
    [Fact]
    public async Task GetRandomGameAsync_SendsExpectedRequestAndDeserializesJson()
    {
        var handler = new TestHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent(
                    """
                    {
                      "id": 220,
                      "name": "Half-Life 2",
                      "playtimeForever": 872,
                      "playtime2Weeks": 192,
                      "rTimeLastPlayed": 1762204440
                    }
                    """)
            });
        var client = CreateClient(handler);

        RandomSteamGameResult result =
            await client.GetRandomGameAsync(
                76561198000000000,
                CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Game);
        Assert.Equal(220, result.Game.Id);
        Assert.Equal("Half-Life 2", result.Game.Name);
        Assert.Equal(HttpMethod.Get, handler.Requests.Single().Method);
        Assert.Equal(
            "/api/steam/random-game/details?userId=76561198000000000",
            handler.Requests.Single().RequestUri?.PathAndQuery);
        Assert.Equal(1, handler.SendCount);
    }

    [Fact]
    public async Task GetRandomGameAsync_NonSuccessStatusReturnsUnavailable()
    {
        var client = CreateClient(
            new TestHttpMessageHandler(
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        RandomSteamGameResult result =
            await client.GetRandomGameAsync(76561198000000000, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Game);
    }

    [Fact]
    public async Task GetRandomGameAsync_EmptyBodyReturnsUnavailable()
    {
        var client = CreateClient(
            new TestHttpMessageHandler(
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent("")
                }));

        RandomSteamGameResult result =
            await client.GetRandomGameAsync(76561198000000000, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetRandomGameAsync_InvalidJsonReturnsUnavailable()
    {
        var client = CreateClient(
            new TestHttpMessageHandler(
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent("{nope")
                }));

        RandomSteamGameResult result =
            await client.GetRandomGameAsync(76561198000000000, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetRandomGameAsync_InvalidGameIdReturnsUnavailable()
    {
        var client = CreateClient(
            new TestHttpMessageHandler(
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent("""{"id":0,"name":"Half-Life 2"}""")
                }));

        RandomSteamGameResult result =
            await client.GetRandomGameAsync(76561198000000000, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetRandomGameAsync_EmptyGameNameReturnsUnavailable()
    {
        var client = CreateClient(
            new TestHttpMessageHandler(
                _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent("""{"id":220,"name":" "}""")
                }));

        RandomSteamGameResult result =
            await client.GetRandomGameAsync(76561198000000000, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetRandomGameAsync_ConnectionFailureReturnsUnavailable()
    {
        var client = CreateClient(
            new TestHttpMessageHandler(
                _ => throw new HttpRequestException("connection failed")));

        RandomSteamGameResult result =
            await client.GetRandomGameAsync(76561198000000000, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetRandomGameAsync_UpstreamTimeoutReturnsUnavailable()
    {
        var client = CreateClient(
            new TestHttpMessageHandler(
                _ => throw new TaskCanceledException("timeout")));

        RandomSteamGameResult result =
            await client.GetRandomGameAsync(76561198000000000, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetRandomGameAsync_CallerCancellationPropagates()
    {
        var client = CreateClient(
            new TestHttpMessageHandler(
                _ => new HttpResponseMessage(HttpStatusCode.OK)));
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetRandomGameAsync(
                76561198000000000,
                cancellationTokenSource.Token));
    }

    [Fact]
    public async Task GetRandomGameAsync_DoesNotRetry()
    {
        var handler = new TestHttpMessageHandler(
            _ => throw new HttpRequestException("connection failed"));
        var client = CreateClient(handler);

        RandomSteamGameResult result =
            await client.GetRandomGameAsync(76561198000000000, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(1, handler.SendCount);
    }

    private static RandomSteamGameClient CreateClient(
        TestHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://randomsteam.example/")
        };

        return new RandomSteamGameClient(
            new TestHttpClientFactory(httpClient),
            NullLogger<RandomSteamGameClient>.Instance);
    }

    private static StringContent JsonContent(string content) =>
        new(
            content,
            Encoding.UTF8,
            "application/json");

    private sealed class TestHttpClientFactory(
        HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TestHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendCount++;
            Requests.Add(request);

            return Task.FromResult(responseFactory(request));
        }
    }
}
