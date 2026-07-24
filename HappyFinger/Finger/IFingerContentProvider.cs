/*
 * Happy Finger Service
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */


namespace HappyFinger.Finger;

public interface IFingerContentProvider
{
    Task<FingerContentResult> GetAsync(
        FingerContentKey key,
        CancellationToken cancellationToken);
}
