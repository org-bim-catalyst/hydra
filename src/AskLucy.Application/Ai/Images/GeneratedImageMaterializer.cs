using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Ai.Images;

/// <summary>A provider returned something that is not a usable image — malformed encoding, an unsupported format, or too large.</summary>
public sealed class InvalidGeneratedImageException(string message) : Exception(message);

/// <summary>
/// Turns any <see cref="GeneratedImagePayload"/> form — hosted URL, base64, data URL, or binary —
/// into one <see cref="GeneratedImage"/>. The format is decided by the content's own signature
/// bytes (PNG, JPEG, WebP: the image formats the document store accepts), so a provider that
/// mislabels its MIME type, or a file that is not an image at all, is caught here rather than
/// stored (constitution: sanitise stored files).
/// </summary>
public sealed class GeneratedImageMaterializer(IRemoteFileDownloader remoteFileDownloader)
{
    /// <summary>Well above any 1024–2048px generation; bounds a runaway or hostile response.</summary>
    public const int MaxImageBytes = 25 * 1024 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] RiffSignature = "RIFF"u8.ToArray();
    private static readonly byte[] WebpSignature = "WEBP"u8.ToArray();

    public async Task<GeneratedImage> MaterializeAsync(GeneratedImagePayload payload, CancellationToken cancellationToken = default)
    {
        var bytes = payload switch
        {
            GeneratedImagePayload.Binary binary => binary.Content,
            GeneratedImagePayload.Base64 base64 => DecodeBase64(base64.Data),
            GeneratedImagePayload.DataUrl dataUrl => DecodeDataUrl(dataUrl.Value),
            GeneratedImagePayload.RemoteUrl remote => await DownloadAsync(remote.Url, cancellationToken),
            _ => throw new InvalidGeneratedImageException($"Unsupported image payload form '{payload.GetType().Name}'."),
        };

        if (bytes.Length == 0)
        {
            throw new InvalidGeneratedImageException("The provider returned an empty image.");
        }

        if (bytes.Length > MaxImageBytes)
        {
            throw new InvalidGeneratedImageException($"The generated image is {bytes.Length} bytes, over the {MaxImageBytes}-byte limit.");
        }

        var (contentType, extension) = DetectFormat(bytes)
            ?? throw new InvalidGeneratedImageException("The provider's response is not a PNG, JPEG or WebP image.");

        return new GeneratedImage(bytes, contentType, extension);
    }

    private static byte[] DecodeBase64(string data)
    {
        try
        {
            // Some providers wrap long base64 across lines; whitespace is never significant.
            return Convert.FromBase64String(string.Concat(data.Where(c => !char.IsWhiteSpace(c))));
        }
        catch (FormatException ex)
        {
            throw new InvalidGeneratedImageException($"The provider's base64 image data is malformed: {ex.Message}");
        }
    }

    private static byte[] DecodeDataUrl(string value)
    {
        // data:[<mediatype>][;base64],<data>
        if (!value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidGeneratedImageException("The provider's data URL does not start with 'data:'.");
        }

        var comma = value.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            throw new InvalidGeneratedImageException("The provider's data URL has no data section.");
        }

        var header = value[5..comma];
        var data = value[(comma + 1)..];

        return header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
            ? DecodeBase64(data)
            : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
    }

    private async Task<byte[]> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        var downloaded = await remoteFileDownloader.DownloadAsync(url, cancellationToken);
        await using var content = downloaded.Content;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static (string ContentType, string Extension)? DetectFormat(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(PngSignature))
        {
            return ("image/png", ".png");
        }

        if (bytes.AsSpan().StartsWith(JpegSignature))
        {
            return ("image/jpeg", ".jpg");
        }

        if (bytes.Length >= 12 && bytes.AsSpan().StartsWith(RiffSignature) && bytes.AsSpan(8, 4).SequenceEqual(WebpSignature))
        {
            return ("image/webp", ".webp");
        }

        return null;
    }
}
