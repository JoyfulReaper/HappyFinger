namespace HappyFinger.Tests;

public sealed class RandomSteamGameResponseFormatterTests
{
    [Theory]
    [InlineData(0, "Unplayed")]
    [InlineData(-1, "Unplayed")]
    [InlineData(1, "1 minute")]
    [InlineData(42, "42 minutes")]
    [InlineData(60, "1h 0m")]
    [InlineData(872, "14h 32m")]
    public void FormatPlaytime_FormatsMinutes(
        int minutes,
        string expected)
    {
        Assert.Equal(
            expected,
            RandomSteamGameResponseFormatter.FormatPlaytime(minutes));
    }

    [Fact]
    public void FormatLastPlayed_FormatsTimestampAsUtc()
    {
        Assert.Equal(
            "2025-11-03 21:14 UTC",
            RandomSteamGameResponseFormatter.FormatLastPlayed(1762204440));
    }

    [Fact]
    public void FormatLastPlayed_HandlesNeverAndOutOfRange()
    {
        Assert.Equal(
            "Never",
            RandomSteamGameResponseFormatter.FormatLastPlayed(0));
        Assert.Equal(
            "Unknown",
            RandomSteamGameResponseFormatter.FormatLastPlayed(long.MaxValue));
    }

    [Fact]
    public void SanitizeGameName_RemovesAnsiAndControls()
    {
        string sanitized =
            RandomSteamGameResponseFormatter.SanitizeGameName(
                "Half-Life\u001b[31m\t2\u0000\nEpisode");

        Assert.Equal("Half-Life 2 Episode", sanitized);
        Assert.DoesNotContain(sanitized, character => character == (char)0x1b);
    }
}
