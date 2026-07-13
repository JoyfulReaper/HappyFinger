namespace HappyFinger;

public sealed record RandomGameDetails
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public int PlaytimeForever { get; init; }

    public int Playtime2Weeks { get; init; }

    public long RTimeLastPlayed { get; init; }
}
