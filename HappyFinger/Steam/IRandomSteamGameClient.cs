/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


namespace HappyFinger.Steam;

public interface IRandomSteamGameClient
{
    Task<RandomSteamGameResult> GetRandomGameAsync(
        long steamId,
        CancellationToken cancellationToken);
}
