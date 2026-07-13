namespace HappyFinger;

public sealed class FingerResponseResolver(
    IPlanFileReader planFileReader,
    IRandomSteamGameClient randomSteamGameClient,
    IFingerContentProvider contentProvider) : IFingerResponseResolver
{
    private const string EmergencyFallback =
        "HappyFinger content is temporarily unavailable.";

    public async Task<FingerResponse> ResolveAsync(
        string? request,
        CancellationToken cancellationToken)
    {
        FingerQuery query =
            FingerQueryParser.Parse(request);

        if (query.Value.Contains('@'))
        {
            return await CreateContentResponseAsync(
                FingerContentKey.ForwardingNotSupported,
                FingerResponseTypes.ForwardingNotSupported,
                cancellationToken);
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

        return await ResolveStaticContentAsync(
            query,
            cancellationToken);
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
            return await CreateContentResponseAsync(
                FingerContentKey.RandomGameUnavailable,
                FingerResponseTypes.RandomGameUnavailable,
                cancellationToken);
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
            return await CreateContentResponseAsync(
                FingerContentKey.NowFallback,
                FingerResponseTypes.Now,
                cancellationToken);
        }

        string content = $"Kyle's Plan\r\n\r\n{result.Content}";

        if (result.Truncated)
        {
            content += "\r\n\r\n[Plan truncated]";
        }

        return FingerResponseFactory.Create(
            FingerResponseTypes.Now,
            content);
    }

    private Task<FingerResponse> ResolveStaticContentAsync(
        FingerQuery query,
        CancellationToken cancellationToken)
    {
        if (query.IsEmpty)
        {
            return CreateContentResponseAsync(
                FingerContentKey.Directory,
                FingerResponseTypes.Directory,
                cancellationToken);
        }

        return query.Value.ToLowerInvariant() switch
        {
            "kyle" => CreateContentResponseAsync(
                FingerContentKey.Kyle,
                FingerResponseTypes.Kyle,
                cancellationToken),
            "projects" => CreateContentResponseAsync(
                FingerContentKey.Projects,
                FingerResponseTypes.Projects,
                cancellationToken),
            "services" => CreateContentResponseAsync(
                FingerContentKey.Services,
                FingerResponseTypes.Services,
                cancellationToken),
            "randomsteam" => CreateContentResponseAsync(
                FingerContentKey.RandomSteam,
                FingerResponseTypes.RandomSteam,
                cancellationToken),
            "reapershell" => CreateContentResponseAsync(
                FingerContentKey.ReaperShell,
                FingerResponseTypes.ReaperShell,
                cancellationToken),
            "help" => CreateContentResponseAsync(
                FingerContentKey.Help,
                FingerResponseTypes.Help,
                cancellationToken),
            "joke" => CreateContentResponseAsync(
                FingerContentKey.Joke,
                FingerResponseTypes.Joke,
                cancellationToken),
            _ => CreateContentResponseAsync(
                FingerContentKey.NotFound,
                FingerResponseTypes.NotFound,
                cancellationToken)
        };
    }

    private async Task<FingerResponse> CreateContentResponseAsync(
        FingerContentKey key,
        string responseType,
        CancellationToken cancellationToken)
    {
        FingerContentResult result =
            await contentProvider.GetAsync(key, cancellationToken);

        return FingerResponseFactory.Create(
            responseType,
            result.Available ? result.Content : EmergencyFallback);
    }
}
