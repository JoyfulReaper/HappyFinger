using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;

namespace HappyFinger.Plan;

public sealed class PlanFileReader(
    IOptions<PlanFileOptions> options,
    ILogger<PlanFileReader> logger) : IPlanFileReader
{
    private static readonly Encoding PlanEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    public async Task<PlanFileResult> ReadAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PlanFileOptions planOptions = options.Value;
        string path = planOptions.Path;
        int maxBytes = planOptions.MaxBytes;

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
            logger.LogDebug("Configured plan file is not available.");
            return Unavailable();
        }
        catch (DirectoryNotFoundException)
        {
            logger.LogDebug("Configured plan file directory is not available.");
            return Unavailable();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(
                exception,
                "Unable to read configured plan file.");
            return Unavailable();
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(
                exception,
                "Configured plan file path is invalid.");
            return Unavailable();
        }
        catch (NotSupportedException exception)
        {
            logger.LogWarning(
                exception,
                "Configured plan file path is invalid.");
            return Unavailable();
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "Unable to read configured plan file.");
            return Unavailable();
        }

        bool truncated = bytesRead > maxBytes;
        int bytesToDecode = Math.Min(bytesRead, maxBytes);
        string content = PlanEncoding.GetString(buffer, 0, bytesToDecode);
        string sanitized = Sanitize(content);

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            logger.LogDebug("Configured plan file is empty.");
            return Unavailable();
        }

        return new PlanFileResult(
            Available: true,
            Content: sanitized,
            Truncated: truncated);
    }

    private static PlanFileResult Unavailable() =>
        new(
            Available: false,
            Content: string.Empty,
            Truncated: false);

    private static string Sanitize(string content)
    {
        var builder = new StringBuilder(content.Length);

        foreach (char character in content)
        {
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
}
