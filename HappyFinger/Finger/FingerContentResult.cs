namespace HappyFinger.Finger;

public readonly record struct FingerContentResult(
    bool Available,
    string Content,
    bool UsedOverride,
    bool Truncated);
