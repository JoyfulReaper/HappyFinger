namespace HappyFinger.Plan;

public readonly record struct PlanFileResult(
    bool Available,
    string Content,
    bool Truncated);
