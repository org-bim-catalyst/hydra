using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace AskLucy.Infrastructure.Documents.Preview;

/// <summary>Thumbnail generation for raster image uploads (FR-043) via SixLabors.ImageSharp — pinned to 3.1.x, not the licensed 4.x line (research.md tasks.md T004 correction note).</summary>
public sealed class ImageThumbnailGenerator : IDocumentPreviewGenerator
{
    private const int ThumbnailMaxDimension = 400;

    public bool CanHandle(DocumentFileType fileType) =>
        fileType is DocumentFileType.Png or DocumentFileType.Jpeg or DocumentFileType.Tiff or DocumentFileType.Bmp or DocumentFileType.Webp;

    public async Task<IReadOnlyList<DocumentPreviewResult>> GenerateAsync(Stream content, DocumentFileType fileType, CancellationToken cancellationToken = default)
    {
        content.Position = 0;
        using var image = await Image.LoadAsync(content, cancellationToken);

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(ThumbnailMaxDimension, ThumbnailMaxDimension),
        }));

        using var thumbnailStream = new MemoryStream();
        await image.SaveAsync(thumbnailStream, new PngEncoder(), cancellationToken);

        return [new DocumentPreviewResult(DocumentPreviewType.Thumbnail, thumbnailStream.ToArray(), PageNumber: null)];
    }
}
