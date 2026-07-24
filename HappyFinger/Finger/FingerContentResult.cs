/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


namespace HappyFinger.Finger;

public readonly record struct FingerContentResult(
    bool Available,
    string Content,
    bool UsedOverride,
    bool Truncated);
