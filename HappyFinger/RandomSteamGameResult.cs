namespace HappyFinger;

public readonly record struct RandomSteamGameResult(
    bool Succeeded,
    RandomGameDetails? Game);
