namespace HappyFinger.Events;

public sealed record FingerRequestCompletedEvent(
    bool RequestReceived,
    string Request,
    int RequestLength,
    string Remote,
    string ResponseType,
    long DurationMilliseconds,
    string Outcome,
    bool Succeeded)
{
    public const string EventName = "happyfinger.request.completed";
}
