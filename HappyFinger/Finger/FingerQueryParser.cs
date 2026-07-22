namespace HappyFinger.Finger;

public static class FingerQueryParser
{
    public static FingerQuery Parse(string? request)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            return new FingerQuery(
                Value: string.Empty,
                Verbose: false);
        }

        string query = request.Trim();

        if (query.Equals(
            "/W",
            StringComparison.OrdinalIgnoreCase))
        {
            return new FingerQuery(
                Value: string.Empty,
                Verbose: true);
        }

        if (query.Length > 2 &&
            query.StartsWith(
                "/W",
                StringComparison.OrdinalIgnoreCase) &&
            char.IsWhiteSpace(query[2]))
        {
            return new FingerQuery(
                Value: query[3..].Trim(),
                Verbose: true);
        }

        return new FingerQuery(
            Value: query,
            Verbose: false);
    }
}
