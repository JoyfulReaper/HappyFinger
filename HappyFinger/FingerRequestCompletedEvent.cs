namespace HappyFinger;

public sealed record FingerRequestCompletedEvent(
    bool RequestReceived,
    int RequestLength,
    string Remote,
    string ResponseType,
    long DurationMilliseconds,
    string Outcome,
    bool Succeeded);
