namespace HappyFinger;

public readonly record struct FingerResponse(
    ReadOnlyMemory<byte> Bytes,
    string Type);
