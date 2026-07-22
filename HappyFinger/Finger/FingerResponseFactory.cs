using System.Text;

namespace HappyFinger.Finger;

public static class FingerResponseFactory
{
    private static readonly Encoding ResponseEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    public static FingerResponse Create(
        string responseType,
        string content)
    {
        string normalized =
            content.ReplaceLineEndings("\r\n");

        if (!normalized.EndsWith("\r\n", StringComparison.Ordinal))
        {
            normalized += "\r\n";
        }

        return new FingerResponse(
            ResponseEncoding.GetBytes(normalized),
            responseType);
    }
}
