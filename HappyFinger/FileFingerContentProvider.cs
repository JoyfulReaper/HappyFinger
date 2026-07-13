using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;

namespace HappyFinger;

public sealed class FileFingerContentProvider : IFingerContentProvider
{
    private static readonly Encoding ContentEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    private readonly IOptions<FingerContentOptions> _options;
    private readonly ILogger<FileFingerContentProvider> _logger;
    private readonly string _packagedDefaultDirectory;

    public FileFingerContentProvider(
        IOptions<FingerContentOptions> options,
        ILogger<FileFingerContentProvider> logger)
        : this(
            options,
            logger,
            Path.Combine(AppContext.BaseDirectory, "content"))
    {
    }

    internal FileFingerContentProvider(
        IOptions<FingerContentOptions> options,
        ILogger<FileFingerContentProvider> logger,
        string packagedDefaultDirectory)
    {
        _options = options;
        _logger = logger;
        _packagedDefaultDirectory = packagedDefaultDirectory;
    }

    public async Task<FingerContentResult> GetAsync(
        FingerContentKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        FingerContentOptions options = _options.Value;
        string fileName = FingerContentFileNames.GetFileName(key);

        if (!string.IsNullOrWhiteSpace(options.OverrideDirectory))
        {
            FingerContentResult overrideResult = await TryReadAsync(
                key,
                Path.Combine(options.OverrideDirectory, fileName),
                options.MaxBytes,
                usedOverride: true,
                cancellationToken);

            if (overrideResult.Available)
            {
                return overrideResult;
            }
        }

        FingerContentResult defaultResult = await TryReadAsync(
            key,
            Path.Combine(_packagedDefaultDirectory, fileName),
            options.MaxBytes,
            usedOverride: false,
            cancellationToken);

        if (!defaultResult.Available)
        {
            _logger.LogWarning(
                "No usable HappyFinger content file is available for {ContentKey}.",
                key);
        }

        return defaultResult;
    }

    private async Task<FingerContentResult> TryReadAsync(
        FingerContentKey key,
        string path,
        int maxBytes,
        bool usedOverride,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[maxBytes + 1];
        int bytesRead = 0;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            while (bytesRead < buffer.Length)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(bytesRead, buffer.Length - bytesRead),
                    cancellationToken);

                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }
        }
        catch (FileNotFoundException)
        {
            LogMissing(key, usedOverride);
            return Unavailable();
        }
        catch (DirectoryNotFoundException)
        {
            LogMissing(key, usedOverride);
            return Unavailable();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            LogUnreadable(exception, key, usedOverride);
            return Unavailable();
        }
        catch (ArgumentException exception)
        {
            LogUnreadable(exception, key, usedOverride);
            return Unavailable();
        }
        catch (NotSupportedException exception)
        {
            LogUnreadable(exception, key, usedOverride);
            return Unavailable();
        }
        catch (IOException exception)
        {
            LogUnreadable(exception, key, usedOverride);
            return Unavailable();
        }

        bool truncated = bytesRead > maxBytes;
        int bytesToDecode = Math.Min(bytesRead, maxBytes);
        string content = ContentEncoding.GetString(buffer, 0, bytesToDecode);
        string sanitized = Sanitize(content);

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            _logger.LogDebug(
                "{ContentSource} HappyFinger content file for {ContentKey} is empty.",
                SourceName(usedOverride),
                key);
            return Unavailable();
        }

        if (truncated)
        {
            sanitized = $"{sanitized}\r\n\r\n[Content truncated]";
        }

        return new FingerContentResult(
            Available: true,
            Content: EnsureFinalCrLf(sanitized),
            UsedOverride: usedOverride,
            Truncated: truncated);
    }

    private void LogMissing(
        FingerContentKey key,
        bool usedOverride) =>
        _logger.LogDebug(
            "{ContentSource} HappyFinger content file for {ContentKey} is not available.",
            SourceName(usedOverride),
            key);

    private void LogUnreadable(
        Exception exception,
        FingerContentKey key,
        bool usedOverride) =>
        _logger.LogWarning(
            exception,
            "Unable to read {ContentSource} HappyFinger content file for {ContentKey}.",
            SourceName(usedOverride),
            key);

    private static FingerContentResult Unavailable() =>
        new(
            Available: false,
            Content: string.Empty,
            UsedOverride: false,
            Truncated: false);

    private static string SourceName(bool usedOverride) =>
        usedOverride ? "override" : "packaged";

    private static string Sanitize(string content)
    {
        var builder = new StringBuilder(content.Length);

        for (int index = 0; index < content.Length; index++)
        {
            char character = content[index];

            if (character == '\u001b')
            {
                index = SkipAnsiEscapeSequence(content, index);
                continue;
            }

            if (character is '\r' or '\n' or '\t')
            {
                builder.Append(character);
                continue;
            }

            UnicodeCategory category = char.GetUnicodeCategory(character);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format)
            {
                continue;
            }

            builder.Append(character);
        }

        return builder
            .ToString()
            .ReplaceLineEndings("\r\n")
            .TrimEnd();
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

    private static string EnsureFinalCrLf(string content)
    {
        string normalized = content.ReplaceLineEndings("\r\n");

        if (!normalized.EndsWith("\r\n", StringComparison.Ordinal))
        {
            normalized += "\r\n";
        }

        return normalized;
    }
}
