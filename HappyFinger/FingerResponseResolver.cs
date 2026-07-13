namespace HappyFinger;

public sealed class FingerResponseResolver(
    IPlanFileReader planFileReader,
    IRandomSteamGameClient randomSteamGameClient) : IFingerResponseResolver
{
    public async Task<FingerResponse> ResolveAsync(
        string? request,
        CancellationToken cancellationToken)
    {
        FingerQuery query =
            FingerQueryParser.Parse(request);

        if (query.Value.Contains('@'))
        {
            return StaticResponses.GetResponse(query);
        }

        if (SteamIdParser.TryParse(
            query.Value,
            out long steamId))
        {
            return await ResolveRandomGameAsync(
                steamId,
                cancellationToken);
        }

        if (query.Value.Equals(
            "now",
            StringComparison.OrdinalIgnoreCase))
        {
            return await ResolvePlanAsync(cancellationToken);
        }

        return StaticResponses.GetResponse(query);
    }

    private async Task<FingerResponse> ResolveRandomGameAsync(
        long steamId,
        CancellationToken cancellationToken)
    {
        RandomSteamGameResult result =
            await randomSteamGameClient.GetRandomGameAsync(
                steamId,
                cancellationToken);

        if (!result.Succeeded || result.Game is null)
        {
            return RandomSteamGameResponseFormatter.CreateUnavailableResponse();
        }

        return RandomSteamGameResponseFormatter.CreateSuccessResponse(result.Game);
    }

    private async Task<FingerResponse> ResolvePlanAsync(
        CancellationToken cancellationToken)
    {
        PlanFileResult result =
            await planFileReader.ReadAsync(cancellationToken);

        if (!result.Available)
        {
            return StaticResponses.GetResponse(
                new FingerQuery(
                    Value: FingerResponseTypes.Now,
                    Verbose: false));
        }

        string content = $"Kyle's Plan\r\n\r\n{result.Content}";

        if (result.Truncated)
        {
            content += "\r\n\r\n[Plan truncated]";
        }

        return StaticResponses.CreateResponse(
            FingerResponseTypes.Now,
            content);
    }
}
