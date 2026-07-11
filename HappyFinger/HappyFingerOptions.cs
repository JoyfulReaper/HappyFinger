/*
 * Happy Finger Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyFinger;

public sealed class HappyFingerOptions
{
    public const string SectionName = "Finger";
    public string ListenAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 79;
    public int MaxConcurrentConnections { get; init; } = 64;
    public int RequestTimeoutSeconds { get; init; } = 15;
}
