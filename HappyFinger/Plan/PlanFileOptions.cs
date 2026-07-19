namespace HappyFinger.Plan;

public sealed class PlanFileOptions
{
    public const string SectionName = "PlanFile";
    public const int MaxAllowedBytes = 1024 * 1024;

    public string Path { get; init; } = "data/.plan";

    public int MaxBytes { get; init; } = 16 * 1024;
}
