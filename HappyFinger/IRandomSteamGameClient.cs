namespace HappyFinger;

public interface IRandomSteamGameClient
{
    Task<RandomSteamGameResult> GetRandomGameAsync(
        long steamId,
        CancellationToken cancellationToken);
}
