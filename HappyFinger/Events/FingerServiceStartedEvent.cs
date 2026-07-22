/*
 * Happy Finger Server
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License.
 */

namespace HappyFinger.Events;

public sealed record FingerServiceStartedEvent(
    string ListenAddress)
{
    public const string EventName = "happyfinger.service.started";
}