namespace HappyFinger;

public interface IFingerResponseResolver
{
    Task<FingerResponse> ResolveAsync(
        string? request,
        CancellationToken cancellationToken);
}
