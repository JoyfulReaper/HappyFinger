/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


namespace HappyFinger.Finger;

public sealed class FingerContentOptions
{
    public const string SectionName = "FingerContent";
    public string? OverrideDirectory { get; init; }
    public int MaxBytes { get; init; } = 16 * 1024;
    public const int MaxAllowedBytes = 1024 * 1024;
}
