namespace HappyFinger.Steam;

public readonly record struct RandomSteamGameResult(
    bool Succeeded,
    RandomGameDetails? Game);
