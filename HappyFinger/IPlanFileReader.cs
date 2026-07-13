namespace HappyFinger;

public interface IPlanFileReader
{
    Task<PlanFileResult> ReadAsync(
        CancellationToken cancellationToken);
}
