namespace HappyFinger.Steam;

public sealed class RandomSteamGameOptions
{
    public const string SectionName = "RandomSteamGame";

    public string BaseUrl { get; init; } =
        "https://randomsteam.kgivler.com/";

    public int TimeoutSeconds { get; init; } = 5;
}
