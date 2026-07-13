namespace HappyFinger;

public readonly record struct FingerContentResult(
    bool Available,
    string Content,
    bool UsedOverride,
    bool Truncated);
