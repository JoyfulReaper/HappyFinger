using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace HappyFinger.Tests;

public sealed class FileFingerContentProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "HappyFinger.Tests",
        Guid.NewGuid().ToString("N"));

    private readonly string _packagedDirectory;
    private readonly string _overrideDirectory;

    public FileFingerContentProviderTests()
    {
        _packagedDirectory = Path.Combine(_root, "packaged");
        _overrideDirectory = Path.Combine(_root, "override");
        Directory.CreateDirectory(_packagedDirectory);
        Directory.CreateDirectory(_overrideDirectory);
    }

    [Fact]
    public async Task GetAsync_ReturnsPackagedContentWhenNoOverrideIsConfigured()
    {
        await WritePackagedAsync(FingerContentKey.Directory, "packaged");

        FingerContentResult result =
            await CreateProvider().GetAsync(FingerContentKey.Directory, CancellationToken.None);

        Assert.True(result.Available);
        Assert.False(result.UsedOverride);
        Assert.Equal("packaged\r\n", result.Content);
    }

    [Fact]
    public async Task GetAsync_ReturnsOverrideContentWhenAvailable()
    {
        await WritePackagedAsync(FingerContentKey.Kyle, "packaged");
        await WriteOverrideAsync(FingerContentKey.Kyle, "override");

        FingerContentResult result =
            await CreateProvider(_overrideDirectory).GetAsync(FingerContentKey.Kyle, CancellationToken.None);

        Assert.True(result.Available);
        Assert.True(result.UsedOverride);
        Assert.Equal("override\r\n", result.Content);
    }

    [Fact]
    public async Task GetAsync_MissingOverrideFallsBackToPackagedContent()
    {
        await WritePackagedAsync(FingerContentKey.Help, "packaged help");

        FingerContentResult result =
            await CreateProvider(_overrideDirectory).GetAsync(FingerContentKey.Help, CancellationToken.None);

        Assert.True(result.Available);
        Assert.False(result.UsedOverride);
        Assert.Equal("packaged help\r\n", result.Content);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public async Task GetAsync_EmptyOrWhitespaceOverrideFallsBackToPackagedContent(
        string overrideContent)
    {
        await WritePackagedAsync(FingerContentKey.Services, "packaged services");
        await WriteOverrideAsync(FingerContentKey.Services, overrideContent);

        FingerContentResult result =
            await CreateProvider(_overrideDirectory).GetAsync(FingerContentKey.Services, CancellationToken.None);

        Assert.True(result.Available);
        Assert.False(result.UsedOverride);
        Assert.Equal("packaged services\r\n", result.Content);
    }

    [Fact]
    public async Task GetAsync_UnreadableOverrideFallsBackWherePractical()
    {
        await WritePackagedAsync(FingerContentKey.Projects, "packaged projects");
        string overridePath = Path.Combine(
            _overrideDirectory,
            FingerContentFileNames.GetFileName(FingerContentKey.Projects));
        Directory.CreateDirectory(overridePath);

        FingerContentResult result =
            await CreateProvider(_overrideDirectory).GetAsync(FingerContentKey.Projects, CancellationToken.None);

        Assert.True(result.Available);
        Assert.False(result.UsedOverride);
        Assert.Equal("packaged projects\r\n", result.Content);
    }

    [Fact]
    public async Task GetAsync_MissingOverrideAndPackagedContentReturnsUnavailable()
    {
        FingerContentResult result =
            await CreateProvider(_overrideDirectory).GetAsync(FingerContentKey.NotFound, CancellationToken.None);

        Assert.False(result.Available);
        Assert.Equal("", result.Content);
    }

    [Fact]
    public async Task GetAsync_ReadsFileAgainAfterItChanges()
    {
        await WriteOverrideAsync(FingerContentKey.Joke, "first");
        FileFingerContentProvider provider = CreateProvider(_overrideDirectory);

        FingerContentResult first =
            await provider.GetAsync(FingerContentKey.Joke, CancellationToken.None);

        await WriteOverrideAsync(FingerContentKey.Joke, "second");

        FingerContentResult second =
            await provider.GetAsync(FingerContentKey.Joke, CancellationToken.None);

        Assert.Equal("first\r\n", first.Content);
        Assert.Equal("second\r\n", second.Content);
    }

    [Fact]
    public async Task GetAsync_DoesNotCachePackagedContent()
    {
        await WritePackagedAsync(FingerContentKey.RandomSteam, "first");
        FileFingerContentProvider provider = CreateProvider();

        FingerContentResult first =
            await provider.GetAsync(FingerContentKey.RandomSteam, CancellationToken.None);

        await WritePackagedAsync(FingerContentKey.RandomSteam, "second");

        FingerContentResult second =
            await provider.GetAsync(FingerContentKey.RandomSteam, CancellationToken.None);

        Assert.Equal("first\r\n", first.Content);
        Assert.Equal("second\r\n", second.Content);
    }

    [Fact]
    public async Task GetAsync_OversizedContentIsBoundedAndReportsTruncation()
    {
        await WritePackagedAsync(FingerContentKey.ReaperShell, "abcdef");

        FingerContentResult result =
            await CreateProvider(maxBytes: 5).GetAsync(FingerContentKey.ReaperShell, CancellationToken.None);

        Assert.True(result.Available);
        Assert.True(result.Truncated);
        Assert.Equal("abcde\r\n\r\n[Content truncated]\r\n", result.Content);
    }

    [Theory]
    [InlineData("one\ntwo", "one\r\ntwo\r\n")]
    [InlineData("one\rtwo", "one\r\ntwo\r\n")]
    [InlineData("one\r\ntwo", "one\r\ntwo\r\n")]
    public async Task GetAsync_NormalizesLineEndings(
        string input,
        string expected)
    {
        await WritePackagedAsync(FingerContentKey.Directory, input);

        FingerContentResult result =
            await CreateProvider().GetAsync(FingerContentKey.Directory, CancellationToken.None);

        Assert.Equal(expected, result.Content);
    }

    [Fact]
    public async Task GetAsync_PreservesTabs()
    {
        await WritePackagedAsync(FingerContentKey.Directory, "one\ttwo");

        FingerContentResult result =
            await CreateProvider().GetAsync(FingerContentKey.Directory, CancellationToken.None);

        Assert.Equal("one\ttwo\r\n", result.Content);
    }

    [Fact]
    public async Task GetAsync_RemovesAnsiEscapeAndUnsafeControlCharacters()
    {
        await WritePackagedAsync(
            FingerContentKey.Directory,
            "safe\u001b[31mred\u001b[0m\0\u0007text");

        FingerContentResult result =
            await CreateProvider().GetAsync(FingerContentKey.Directory, CancellationToken.None);

        Assert.Equal("saferedtext\r\n", result.Content);
    }

    [Fact]
    public async Task GetAsync_RemovesUnicodeFormattingControls()
    {
        await WritePackagedAsync(FingerContentKey.Directory, "a\u200Eb");

        FingerContentResult result =
            await CreateProvider().GetAsync(FingerContentKey.Directory, CancellationToken.None);

        Assert.Equal("ab\r\n", result.Content);
    }

    [Fact]
    public async Task GetAsync_PreservesOrdinaryUnicodeText()
    {
        await WritePackagedAsync(FingerContentKey.Directory, "Cafe \u2603");

        FingerContentResult result =
            await CreateProvider().GetAsync(FingerContentKey.Directory, CancellationToken.None);

        Assert.Equal("Cafe \u2603\r\n", result.Content);
    }

    [Fact]
    public async Task GetAsync_PropagatesCancellation()
    {
        await WritePackagedAsync(FingerContentKey.Directory, "cancel me");
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateProvider().GetAsync(
                FingerContentKey.Directory,
                cancellationTokenSource.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private Task WritePackagedAsync(
        FingerContentKey key,
        string content) =>
        WriteContentAsync(_packagedDirectory, key, content);

    private Task WriteOverrideAsync(
        FingerContentKey key,
        string content) =>
        WriteContentAsync(_overrideDirectory, key, content);

    private static Task WriteContentAsync(
        string directory,
        FingerContentKey key,
        string content) =>
        File.WriteAllTextAsync(
            Path.Combine(directory, FingerContentFileNames.GetFileName(key)),
            content,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: false));

    private FileFingerContentProvider CreateProvider(
        string? overrideDirectory = null,
        int maxBytes = 16 * 1024) =>
        new(
            Options.Create(
                new FingerContentOptions
                {
                    OverrideDirectory = overrideDirectory,
                    MaxBytes = maxBytes
                }),
            NullLogger<FileFingerContentProvider>.Instance,
            _packagedDirectory);
}
