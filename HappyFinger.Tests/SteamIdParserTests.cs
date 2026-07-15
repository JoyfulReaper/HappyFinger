using HappyFinger.Steam;

namespace HappyFinger.Tests;

public sealed class SteamIdParserTests
{
    public static TheoryData<string, bool> SteamIds => new()
    {
        { "76561198000000000", true },
        { "10000000000000000", true },
        { "99999999999999999", true },
        { "09999999999999999", false },
        { "7656119800000000", false },
        { "765611980000000000", false },
        { "+76561198000000000", false },
        { "-76561198000000000", false },
        { "7656119800000000x", false },
        { "7656 1198 0000 0000", false },
        { "", false }
    };

    [Theory]
    [MemberData(nameof(SteamIds))]
    public void TryParse_ValidatesSteamId(
        string value,
        bool expected)
    {
        bool parsed = SteamIdParser.TryParse(value, out long steamId);

        Assert.Equal(expected, parsed);
        if (expected)
        {
            Assert.True(steamId > 0);
        }
        else
        {
            Assert.Equal(0, steamId);
        }
    }
}
