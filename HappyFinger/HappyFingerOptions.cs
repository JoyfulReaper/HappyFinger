/*
 * Happy Finger Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyFinger;

public sealed class HappyFingerOptions
{
    public const string SectionName = "Finger";
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 79;
    public int MaxConcurrentConnections { get; set; } = 64;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public string? TelemetryIgnoredRemoteAddress { get; set; }
}
