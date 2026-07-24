/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


namespace HappyFinger.Finger;

public readonly record struct FingerResponse(
    ReadOnlyMemory<byte> Bytes,
    string Type);
