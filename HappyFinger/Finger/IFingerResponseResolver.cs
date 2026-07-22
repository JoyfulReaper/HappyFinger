namespace HappyFinger.Finger;

public interface IFingerResponseResolver
{
    Task<FingerResponse> ResolveAsync(
        string? request,
        CancellationToken cancellationToken);
}
