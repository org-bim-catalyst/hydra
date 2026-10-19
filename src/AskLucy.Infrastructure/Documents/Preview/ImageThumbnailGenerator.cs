using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using ImageMagick;

namespace AskLucy.Infrastructure.Documents.Preview;

/// <summary>Thumbnail generation for raster image uploads (FR-043) via Magick.NET (Apache-2.0).</summary>
public sealed class ImageThumbnailGenerator : IDocumentPreviewGenerator
{
    private const int ThumbnailMaxDimension = 400;

    public bool CanHandle(DocumentFileType fileType) =>
        fileType is DocumentFileType.Png or DocumentFileType.Jpeg or DocumentFileType.Tiff or DocumentFileType.Bmp or DocumentFileType.Webp;

    public Task<IReadOnlyList<DocumentPreviewResult>> GenerateAsync(Stream content, DocumentFileType fileType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        content.Position = 0;

        using var image = new MagickImage(content);
        // Greater = only shrink, never enlarge — the same bar ImageSharp's ResizeMode.Max applied.
        image.Resize(new MagickGeometry(ThumbnailMaxDimension, ThumbnailMaxDimension) { Greater = true });

        using var thumbnailStream = new MemoryStream();
        image.Write(thumbnailStream, MagickFormat.Png);

        return Task.FromResult<IReadOnlyList<DocumentPreviewResult>>(
            [new DocumentPreviewResult(DocumentPreviewType.Thumbnail, thumbnailStream.ToArray(), PageNumber: null)]);
    }
}
