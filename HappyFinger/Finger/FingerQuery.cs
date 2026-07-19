namespace HappyFinger.Finger;

public readonly record struct FingerQuery(
    string Value,
    bool Verbose)
{
    public bool IsEmpty =>
        string.IsNullOrEmpty(Value);
}
