namespace HappyFinger;

public sealed record FingerRequestCompletedEvent(
    bool RequestReceived,
    int RequestLength,
    string Remote,
    long DurationMilliseconds,
    string Outcome,
    bool Succeeded);