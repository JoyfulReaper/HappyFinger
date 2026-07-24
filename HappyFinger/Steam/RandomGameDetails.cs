/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


namespace HappyFinger.Steam;

public sealed record RandomGameDetails
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PlaytimeForever { get; set; }
    public int Playtime2Weeks { get; set; }
    public long RTimeLastPlayed { get; set; }
}
