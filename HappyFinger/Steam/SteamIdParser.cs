using System.Globalization;

namespace HappyFinger.Steam;

internal static class SteamIdParser
{
    private const long MinSteamId = 10_000_000_000_000_000L;
    private const long MaxSteamId = 99_999_999_999_999_999L;

    public static bool TryParse(
        string value,
        out long steamId)
    {
        steamId = 0;

        if (value.Length != 17)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        if (!long.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out steamId))
        {
            return false;
        }

        if (steamId is >= MinSteamId and <= MaxSteamId)
        {
            return true;
        }

        steamId = 0;
        return false;
    }
}
