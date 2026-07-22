namespace HappyFinger.Steam;

public interface IRandomSteamGameClient
{
    Task<RandomSteamGameResult> GetRandomGameAsync(
        long steamId,
        CancellationToken cancellationToken);
}
