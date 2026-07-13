namespace HappyFinger;

public static class FingerContentFileNames
{
    public static string GetFileName(FingerContentKey key) =>
        key switch
        {
            FingerContentKey.Directory => "directory.txt",
            FingerContentKey.Kyle => "kyle.txt",
            FingerContentKey.Projects => "projects.txt",
            FingerContentKey.Services => "services.txt",
            FingerContentKey.RandomSteam => "randomsteam.txt",
            FingerContentKey.ReaperShell => "reapershell.txt",
            FingerContentKey.Help => "help.txt",
            FingerContentKey.Joke => "joke.txt",
            FingerContentKey.NotFound => "not-found.txt",
            FingerContentKey.ForwardingNotSupported => "forwarding-not-supported.txt",
            FingerContentKey.NowFallback => "now-fallback.txt",
            FingerContentKey.RandomGameUnavailable => "random-game-unavailable.txt",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown Finger content key.")
        };
}
