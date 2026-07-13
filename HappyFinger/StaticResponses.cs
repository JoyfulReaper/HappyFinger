using System.Text;

namespace HappyFinger;

public static class StaticResponses
{
    private static readonly Encoding ResponseEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    public static readonly ReadOnlyMemory<byte> DirectoryResponseBytes =
        Encode(
            """
            HappyFinger Public Directory

            Login         Description
            ------------  ------------------------------------------
            kyle          About Kyle Givler
            now           What Kyle is currently working on
            projects      Current software projects
            services      Public services running on this server
            randomsteam   Random Steam Game project information
            reapershell   ReaperShell project information
            help          HappyFinger usage information

            Usage:
              finger <login>@finger.kgivler.com
              finger -l <login>@finger.kgivler.com

            """);

    public static readonly ReadOnlyMemory<byte> KyleResponseBytes =
        Encode(
            """
            Login: kyle
            Name: Kyle Givler
            Website: https://kgivler.com
            GitHub: https://github.com/JoyfulReaper
            Preferred language: C#
            Interests: Backend development, networking, old protocols,
                       game tools, and RimWorld modding

            Project:
              Building small, fast, useful software under JoyfulReaper.

            Plan:
              Improve HappyFinger and keep making strange Internet services.

            """);

    public static readonly ReadOnlyMemory<byte> NowResponseBytes =
        Encode(
            """
            Kyle's Current Plan

            Recently completed:
              - Migrated VPS services from systemd to Docker
              - Added Mission Control telemetry
              - Added Uptime Kuma telemetry filtering
              - Made HappyFinger useful beyond one joke

            Currently working on:
              - Building a public Finger directory
              - Adding useful Finger records
              - Planning support for a traditional .plan file

            """);

    public static readonly ReadOnlyMemory<byte> ProjectsResponseBytes =
        Encode(
            """
            Current Projects

            Random Steam Game
              A fast random game picker for large Steam libraries.

            ReaperShell
              A .NET shell with scripting and multi-language command packs.

            Happy Protocol Servers
              Small implementations of classic Internet protocols including
              Finger, Gopher, and Echo.

            RimWorld Mods
              Performance improvements, compatibility fixes, and modding
              framework experiments.

            More:
              https://github.com/JoyfulReaper

            """);

    public static readonly ReadOnlyMemory<byte> ServicesResponseBytes =
        Encode(
            """
            Public Services

            Finger
              finger.kgivler.com:79
              Public directory and project information.

            Gopher
              gopher.kgivler.com:70
              Classic Gopher content server.

            Echo
              echo.kgivler.com:7
              TCP Echo Protocol server.

            Random Steam Game
              https://randomsteam.kgivler.com
              Random game picker for Steam libraries.

            Website
              https://kgivler.com

            These services are monitored and report operational events to
            Mission Control.

            """);

    public static readonly ReadOnlyMemory<byte> RandomSteamResponseBytes =
        Encode(
            """
            Project: Random Steam Game

            A fast random game picker designed for large Steam libraries.

            Features:
              - Steam ID and vanity URL support
              - Fast cached library retrieval
              - Unplayed-only filtering
              - Temporary game exclusions
              - Production telemetry through Mission Control

            Website:
              https://randomsteam.kgivler.com

            Source:
              https://github.com/JoyfulReaper/RandomSteamGame

            """);

    public static readonly ReadOnlyMemory<byte> ReaperShellResponseBytes =
        Encode(
            """
            Project: ReaperShell

            A local .NET shell with scripting and extensible command packs.

            Features:
              - Custom shell commands
              - Script execution
              - Plugin discovery
              - C# command packs
              - F# command packs
              - VB.NET command packs
              - Unix-inspired built-in commands

            Source:
              https://github.com/JoyfulReaper/ReaperShell

            Status:
              Experimental and under active development.

            """);

    public static readonly ReadOnlyMemory<byte> HelpResponseBytes =
        Encode(
            """
            HappyFinger Help

            Query the public directory:
              finger @finger.kgivler.com

            Query a specific record:
              finger kyle@finger.kgivler.com
              finger projects@finger.kgivler.com
              finger services@finger.kgivler.com

            Request verbose output:
              finger -l kyle@finger.kgivler.com

            Available records:
              kyle
              now
              projects
              services
              randomsteam
              reapershell
              help

            Finger request forwarding is not supported.

            """);

    public static readonly ReadOnlyMemory<byte> ForwardingNotSupportedResponseBytes =
        Encode(
            """
            Finger request forwarding is not supported.

            Query one of the local HappyFinger records instead:
              kyle
              now
              projects
              services
              randomsteam
              reapershell
              help

            """);

    public static readonly ReadOnlyMemory<byte> NotFoundResponseBytes =
        Encode(
            """
            No matching HappyFinger record was found.

            Send an empty query to view the directory, or query:
              help

            """);

    public static readonly ReadOnlyMemory<byte> JokeResponseBytes =
        Encode(
            """
            You fingered me! How dare you!

            """);

    public static ReadOnlyMemory<byte> GetResponse(string? request)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            return DirectoryResponseBytes;
        }

        string query = request.Trim();

        if (query.StartsWith("/W", StringComparison.OrdinalIgnoreCase))
        {
            query = query[2..].Trim();
        }

        if (query.Length == 0)
        {
            return DirectoryResponseBytes;
        }

        if (query.Contains('@'))
        {
            return ForwardingNotSupportedResponseBytes;
        }

        return query.ToLowerInvariant() switch
        {
            "kyle" => KyleResponseBytes,
            "now" => NowResponseBytes,
            "projects" => ProjectsResponseBytes,
            "services" => ServicesResponseBytes,
            "randomsteam" => RandomSteamResponseBytes,
            "reapershell" => ReaperShellResponseBytes,
            "help" => HelpResponseBytes,
            "joke" => JokeResponseBytes,
            _ => NotFoundResponseBytes
        };
    }

    private static ReadOnlyMemory<byte> Encode(string response) =>
        ResponseEncoding.GetBytes(
            response.ReplaceLineEndings("\r\n"));
}