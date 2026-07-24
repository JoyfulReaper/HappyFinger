/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


namespace HappyFinger.Finger;

public interface IFingerResponseResolver
{
    Task<FingerResponse> ResolveAsync(
        string? request,
        CancellationToken cancellationToken);
}
