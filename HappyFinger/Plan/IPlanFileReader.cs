namespace HappyFinger.Plan;

public interface IPlanFileReader
{
    Task<PlanFileResult> ReadAsync(
        CancellationToken cancellationToken);
}
