using System.Text;

namespace HappyFinger.Tests;

public sealed class StaticResponsesTests
{
    public static TheoryData<string?, string, string> Routes => new()
    {
        { "", FingerResponseTypes.Directory, "HappyFinger Public Directory" },
        { "   ", FingerResponseTypes.Directory, "HappyFinger Public Directory" },
        { "/W", FingerResponseTypes.Directory, "HappyFinger Public Directory" },
        { "/W kyle", FingerResponseTypes.Kyle, "Login: kyle" },
        { "/W\tkyle", FingerResponseTypes.Kyle, "Login: kyle" },
        { "/Wrong", FingerResponseTypes.NotFound, "No matching HappyFinger record was found." },
        { "/Whatever", FingerResponseTypes.NotFound, "No matching HappyFinger record was found." },
        { "/Wkyle", FingerResponseTypes.NotFound, "No matching HappyFinger record was found." },
        { "kyle", FingerResponseTypes.Kyle, "Login: kyle" },
        { "KYLE", FingerResponseTypes.Kyle, "Login: kyle" },
        { "now", FingerResponseTypes.Now, "Kyle's Current Plan" },
        { "projects", FingerResponseTypes.Projects, "Current Projects" },
        { "services", FingerResponseTypes.Services, "Public Services" },
        { "randomsteam", FingerResponseTypes.RandomSteam, "Project: Random Steam Game" },
        { "reapershell", FingerResponseTypes.ReaperShell, "Project: ReaperShell" },
        { "help", FingerResponseTypes.Help, "HappyFinger Help" },
        { "joke", FingerResponseTypes.Joke, "You fingered me!" },
        { "user@example.com", FingerResponseTypes.ForwardingNotSupported, "Finger request forwarding is not supported." },
        { "unknown", FingerResponseTypes.NotFound, "No matching HappyFinger record was found." }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void GetResponse_ReturnsExpectedTypeAndContent(
        string? request,
        string expectedType,
        string expectedContent)
    {
        FingerResponse response = StaticResponses.GetResponse(request);

        Assert.Equal(expectedType, response.Type);
        Assert.Contains(
            expectedContent,
            Encoding.UTF8.GetString(response.Bytes.Span));
    }

    [Fact]
    public void GetResponse_UsesCrLfLineEndingsWithoutBareLf()
    {
        FingerResponse response = StaticResponses.GetResponse("kyle");
        string text = Encoding.UTF8.GetString(response.Bytes.Span);

        Assert.Contains("\r\n", text);
        Assert.DoesNotContain(
            "\n",
            text.Replace("\r\n", string.Empty));
    }
}
