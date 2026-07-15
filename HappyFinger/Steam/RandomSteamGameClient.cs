using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HappyFinger.Steam;

public sealed class RandomSteamGameClient(
    IHttpClientFactory httpClientFactory,
    ILogger<RandomSteamGameClient> logger) : IRandomSteamGameClient
{
    public const string HttpClientName = "RandomSteamGame";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<RandomSteamGameResult> GetRandomGameAsync(
        long steamId,
        CancellationToken cancellationToken)
    {
        using HttpClient client =
            httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/steam/random-game/details?userId={steamId}");

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using HttpResponseMessage response =
                await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Random Steam Game returned HTTP status {StatusCode}.",
                    (int)response.StatusCode);

                return Unavailable();
            }

            RandomGameDetails? game =
                await response.Content.ReadFromJsonAsync<RandomGameDetails>(
                    JsonOptions,
                    cancellationToken);

            if (game is null || game.Id <= 0 || string.IsNullOrWhiteSpace(game.Name))
            {
                logger.LogWarning(
                    "Random Steam Game returned an unusable game payload.");

                return Unavailable();
            }

            return new RandomSteamGameResult(
                Succeeded: true,
                Game: game);
        }
        catch (OperationCanceledException)
        when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Random Steam Game request timed out.");

            return Unavailable();
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Random Steam Game request failed.");

            return Unavailable();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Random Steam Game returned invalid JSON.");

            return Unavailable();
        }
        catch (NotSupportedException exception)
        {
            logger.LogWarning(
                exception,
                "Random Steam Game response could not be read.");

            return Unavailable();
        }
    }

    private static RandomSteamGameResult Unavailable() =>
        new(
            Succeeded: false,
            Game: null);
}
