/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


namespace HappyFinger.Finger;

public readonly record struct FingerQuery(string Value, bool Verbose)
{
    public bool IsEmpty =>
        string.IsNullOrEmpty(Value);
}
