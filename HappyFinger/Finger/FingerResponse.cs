namespace HappyFinger.Finger;

public readonly record struct FingerResponse(
    ReadOnlyMemory<byte> Bytes,
    string Type);
