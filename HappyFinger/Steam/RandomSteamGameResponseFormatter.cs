using HappyFinger.Finger;
using System.Globalization;
using System.Text;

namespace HappyFinger.Steam;

internal static class RandomSteamGameResponseFormatter
{
    public static FingerResponse CreateSuccessResponse(
        RandomGameDetails game)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Random Steam Game");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Game:        {SanitizeGameName(game.Name)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Steam App:   {game.Id}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Playtime:    {FormatPlaytime(game.PlaytimeForever)}");

        if (game.Playtime2Weeks > 0)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Last two weeks: {FormatPlaytime(game.Playtime2Weeks)}");
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"Last played: {FormatLastPlayed(game.RTimeLastPlayed)}");
        builder.AppendLine();
        builder.AppendLine("Store:");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  https://store.steampowered.com/app/{game.Id}");
        builder.AppendLine();
        builder.AppendLine("Pick another:");
        builder.AppendLine("  finger <steamId>@finger.kgivler.com");

        return FingerResponseFactory.Create(
            FingerResponseTypes.RandomGame,
            builder.ToString());
    }

    internal static string FormatPlaytime(int minutes) =>
        minutes switch
        {
            <= 0 => "Unplayed",
            1 => "1 minute",
            < 60 => string.Create(
                CultureInfo.InvariantCulture,
                $"{minutes} minutes"),
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"{minutes / 60}h {minutes % 60}m")
        };

    internal static string FormatLastPlayed(long unixSeconds)
    {
        if (unixSeconds <= 0)
        {
            return "Never";
        }

        try
        {
            return DateTimeOffset
                .FromUnixTimeSeconds(unixSeconds)
                .UtcDateTime
                .ToString(
                    "yyyy-MM-dd HH:mm 'UTC'",
                    CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "Unknown";
        }
    }

    internal static string SanitizeGameName(string name)
    {
        var builder = new StringBuilder(name.Length);
        bool previousWasWhitespace = false;

        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];

            if (character == '\u001b')
            {
                index = SkipAnsiEscapeSequence(name, index);
                continue;
            }

            if (character is '\r' or '\n' or '\t' || char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            var category = char.GetUnicodeCategory(character);
            if (category is System.Globalization.UnicodeCategory.Control
                or System.Globalization.UnicodeCategory.Format)
            {
                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static int SkipAnsiEscapeSequence(
        string value,
        int escapeIndex)
    {
        int index = escapeIndex + 1;

        if (index < value.Length && value[index] == '[')
        {
            index++;
            while (index < value.Length)
            {
                char character = value[index];
                if (character is >= '@' and <= '~')
                {
                    return index;
                }

                index++;
            }
        }

        return escapeIndex;
    }
}
