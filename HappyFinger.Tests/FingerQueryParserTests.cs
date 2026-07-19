using HappyFinger.Finger;

namespace HappyFinger.Tests;

public sealed class FingerQueryParserTests
{
    public static TheoryData<string?, string, bool> Queries => new()
    {
        { null, "", false },
        { "", "", false },
        { "   ", "", false },
        { "kyle", "kyle", false },
        { "  kyle  ", "kyle", false },
        { "/W", "", true },
        { "/w", "", true },
        { "/W kyle", "kyle", true },
        { "/w KYLE", "KYLE", true },
        { "/W    kyle", "kyle", true },
        { "/W\tkyle", "kyle", true },
        { "/Wrong", "/Wrong", false },
        { "/Whatever", "/Whatever", false },
        { "/Wkyle", "/Wkyle", false }
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public void Parse_ReturnsExpectedQuery(
        string? request,
        string expectedValue,
        bool expectedVerbose)
    {
        FingerQuery query = FingerQueryParser.Parse(request);

        Assert.Equal(expectedValue, query.Value);
        Assert.Equal(expectedVerbose, query.Verbose);
        Assert.Equal(string.IsNullOrEmpty(expectedValue), query.IsEmpty);
    }
}
