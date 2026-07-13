namespace HappyFinger;

public sealed class FingerResponseResolver(
    IPlanFileReader planFileReader) : IFingerResponseResolver
{
    public async Task<FingerResponse> ResolveAsync(
        string? request,
        CancellationToken cancellationToken)
    {
        FingerQuery query =
            FingerQueryParser.Parse(request);

        if (query.Value.Equals(
            "now",
            StringComparison.OrdinalIgnoreCase))
        {
            return await ResolvePlanAsync(cancellationToken);
        }

        return StaticResponses.GetResponse(query);
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
