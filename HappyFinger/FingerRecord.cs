namespace HappyFinger;

public sealed record FingerRecord(
    string Name,
    string DisplayName,
    string Summary,
    string? Project,
    string? Plan,
    IReadOnlyDictionary<string, string> Fields);