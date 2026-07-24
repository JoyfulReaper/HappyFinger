/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyFinger.Plan;

public readonly record struct PlanFileResult(
    bool Available,
    string Content,
    bool Truncated);
