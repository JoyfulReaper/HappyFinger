using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace HappyFinger.Tests;

public sealed class PlanFileReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "HappyFinger.Tests",
        Guid.NewGuid().ToString("N"));

    public PlanFileReaderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task ReadAsync_ReturnsExistingUtf8PlanFile()
    {
        string path = CreatePath();
        await File.WriteAllTextAsync(
            path,
            "Building HappyFinger into a useful public directory.",
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: false));

        PlanFileResult result =
            await CreateReader(path).ReadAsync(CancellationToken.None);

        Assert.True(result.Available);
        Assert.False(result.Truncated);
        Assert.Equal(
            "Building HappyFinger into a useful public directory.",
            result.Content);
    }

    [Fact]
    public async Task ReadAsync_MissingFileReportsUnavailable()
    {
        PlanFileResult result =
            await CreateReader(CreatePath()).ReadAsync(CancellationToken.None);

        Assert.False(result.Available);
        Assert.Equal("", result.Content);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task ReadAsync_EmptyFileReportsUnavailable()
    {
        string path = CreatePath();
        await File.WriteAllTextAsync(path, "");

        PlanFileResult result =
            await CreateReader(path).ReadAsync(CancellationToken.None);

        Assert.False(result.Available);
    }

    [Fact]
    public async Task ReadAsync_WhitespaceOnlyFileReportsUnavailable()
    {
        string path = CreatePath();
        await File.WriteAllTextAsync(path, " \t\r\n ");

        PlanFileResult result =
            await CreateReader(path).ReadAsync(CancellationToken.None);

        Assert.False(result.Available);
    }

    [Fact]
    public async Task ReadAsync_OversizedFileIsBoundedAndReportsTruncation()
    {
        string path = CreatePath();
        await File.WriteAllTextAsync(path, "abcdef");

        PlanFileResult result =
            await CreateReader(path, maxBytes: 5).ReadAsync(CancellationToken.None);

        Assert.True(result.Available);
        Assert.True(result.Truncated);
        Assert.Equal("abcde", result.Content);
    }

    [Fact]
    public async Task ReadAsync_NormalizesCrLfLfAndCrLineEndings()
    {
        string path = CreatePath();
        await File.WriteAllTextAsync(path, "one\r\ntwo\nthree\rfour");

        PlanFileResult result =
            await CreateReader(path).ReadAsync(CancellationToken.None);

        Assert.True(result.Available);
        Assert.Equal("one\r\ntwo\r\nthree\r\nfour", result.Content);
    }

    [Fact]
    public async Task ReadAsync_RemovesAnsiEscapeCharacters()
    {
        string path = CreatePath();
        await File.WriteAllTextAsync(path, "safe\u001b[31mred\u001b[0m");

        PlanFileResult result =
            await CreateReader(path).ReadAsync(CancellationToken.None);

        Assert.True(result.Available);
        Assert.Equal("safe[31mred[0m", result.Content);
    }

    [Fact]
    public async Task ReadAsync_RemovesNulAndUnsafeControls()
    {
        string path = CreatePath();
        await File.WriteAllTextAsync(path, "a\0b\u0007c");

        PlanFileResult result =
            await CreateReader(path).ReadAsync(CancellationToken.None);

        Assert.True(result.Available);
        Assert.Equal("abc", result.Content);
    }

    [Fact]
    public async Task ReadAsync_PreservesTabs()
    {
        string path = CreatePath();
        await File.WriteAllTextAsync(path, "one\ttwo");

        PlanFileResult result =
            await CreateReader(path).ReadAsync(CancellationToken.None);

        Assert.True(result.Available);
        Assert.Equal("one\ttwo", result.Content);
    }

    [Fact]
    public async Task ReadAsync_PropagatesCancellation()
    {
        string path = CreatePath();
        await File.WriteAllTextAsync(path, "cancel me");
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateReader(path).ReadAsync(cancellationTokenSource.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string CreatePath() =>
        Path.Combine(_directory, Guid.NewGuid().ToString("N"));

    private static PlanFileReader CreateReader(
        string path,
        int maxBytes = 16 * 1024) =>
        new(
            Options.Create(
                new PlanFileOptions
                {
                    Path = path,
                    MaxBytes = maxBytes
                }),
            NullLogger<PlanFileReader>.Instance);
}
