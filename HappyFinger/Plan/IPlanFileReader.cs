/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


namespace HappyFinger.Plan;

public interface IPlanFileReader
{
    Task<PlanFileResult> ReadAsync(
        CancellationToken cancellationToken);
}
