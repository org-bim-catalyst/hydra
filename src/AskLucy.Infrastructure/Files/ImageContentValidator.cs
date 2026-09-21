using AskLucy.Application.Abstractions;

namespace AskLucy.Infrastructure.Files;

/// <summary>
/// Magic-byte signature checks for the image formats Ask Lucy accepts as an avatar. Deliberately
/// ignores any caller-supplied extension or <c>Content-Type</c> (constitution §8) — only the
/// actual leading bytes of the content decide whether it is a real image.
/// </summary>
public sealed class ImageContentValidator : IImageContentValidator
{
    private const int SignatureBufferLength = 12;

    public bool IsValidImage(Stream content, out string? detectedContentType)
    {
        var originalPosition = content.CanSeek ? content.Position : 0;

        Span<byte> buffer = stackalloc byte[SignatureBufferLength];
        var bytesRead = ReadFully(content, buffer);

        if (content.CanSeek)
        {
            content.Position = originalPosition;
        }

        var signature = buffer[..bytesRead];

        if (StartsWith(signature, [0xFF, 0xD8, 0xFF]))
        {
            detectedContentType = "image/jpeg";
            return true;
        }

        if (StartsWith(signature, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            detectedContentType = "image/png";
            return true;
        }

        if (StartsWith(signature, "GIF87a"u8) || StartsWith(signature, "GIF89a"u8))
        {
            detectedContentType = "image/gif";
            return true;
        }

        if (signature.Length >= 12 && StartsWith(signature, "RIFF"u8) && signature[8..12].SequenceEqual("WEBP"u8))
        {
            detectedContentType = "image/webp";
            return true;
        }

        detectedContentType = null;
        return false;
    }

    private static int ReadFully(Stream content, Span<byte> buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = content.Read(buffer[totalRead..]);
            if (read == 0) break;
            totalRead += read;
        }

        return totalRead;
    }

    private static bool StartsWith(ReadOnlySpan<byte> signature, ReadOnlySpan<byte> prefix) =>
        signature.Length >= prefix.Length && signature[..prefix.Length].SequenceEqual(prefix);
}
