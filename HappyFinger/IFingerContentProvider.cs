namespace HappyFinger;

public interface IFingerContentProvider
{
    Task<FingerContentResult> GetAsync(
        FingerContentKey key,
        CancellationToken cancellationToken);
}
