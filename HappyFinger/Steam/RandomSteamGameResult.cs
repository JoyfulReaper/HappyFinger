/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


namespace HappyFinger.Steam;

public readonly record struct RandomSteamGameResult(
    bool Succeeded,
    RandomGameDetails? Game);
