using HappyFinger.Finger;

namespace HappyFinger.Tests;

public sealed class FingerContentFileNamesTests
{
    public static TheoryData<FingerContentKey, string> ExpectedMappings => new()
    {
        { FingerContentKey.Directory, "directory.txt" },
        { FingerContentKey.Kyle, "kyle.txt" },
        { FingerContentKey.Projects, "projects.txt" },
        { FingerContentKey.Services, "services.txt" },
        { FingerContentKey.RandomSteam, "randomsteam.txt" },
        { FingerContentKey.ReaperShell, "reapershell.txt" },
        { FingerContentKey.Help, "help.txt" },
        { FingerContentKey.Joke, "joke.txt" },
        { FingerContentKey.NotFound, "not-found.txt" },
        { FingerContentKey.ForwardingNotSupported, "forwarding-not-supported.txt" },
        { FingerContentKey.NowFallback, "now-fallback.txt" },
        { FingerContentKey.RandomGameUnavailable, "random-game-unavailable.txt" }
    };

    [Theory]
    [MemberData(nameof(ExpectedMappings))]
    public void GetFileName_ReturnsExpectedFixedFileName(
        FingerContentKey key,
        string expectedFileName)
    {
        Assert.Equal(expectedFileName, FingerContentFileNames.GetFileName(key));
    }

    [Fact]
    public void GetFileName_MapsEveryKeyExactlyOnce()
    {
        FingerContentKey[] keys = Enum.GetValues<FingerContentKey>();
        string[] fileNames = keys
            .Select(FingerContentFileNames.GetFileName)
            .ToArray();

        Assert.Equal(ExpectedMappings.Count, keys.Length);
        Assert.Equal(fileNames.Length, fileNames.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void GetFileName_ReturnsRelativeFileNamesOnly()
    {
        foreach (FingerContentKey key in Enum.GetValues<FingerContentKey>())
        {
            string fileName = FingerContentFileNames.GetFileName(key);

            Assert.Equal(Path.GetFileName(fileName), fileName);
            Assert.False(Path.IsPathRooted(fileName));
            Assert.DoesNotContain("..", fileName, StringComparison.Ordinal);
            Assert.DoesNotContain(Path.DirectorySeparatorChar, fileName);
            Assert.DoesNotContain(Path.AltDirectorySeparatorChar, fileName);
        }
    }

    [Fact]
    public void GetFileName_RejectsValuesOutsideControlledKeySet()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FingerContentFileNames.GetFileName((FingerContentKey)999));
    }
}
