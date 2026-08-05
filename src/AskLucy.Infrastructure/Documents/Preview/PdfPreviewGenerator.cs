using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using Docnet.Core;
using Docnet.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AskLucy.Infrastructure.Documents.Preview;

/// <summary>
/// PDF page rasterization for previews (FR-043, research.md Decision 6) via <c>Docnet.Core</c> —
/// the same library <see cref="AskLucy.Infrastructure.Documents.Extraction.DocnetPdfTextExtractor"/>
/// uses for text extraction. Originally implemented with <c>PDFtoImage</c> (a separate PDFium
/// binding); replaced after discovering it and <c>Docnet.Core</c> each ship a native
/// <c>pdfium.dll</c> under the identical filename, so only one survives the build's file copy —
/// Docnet.Core silently ended up calling into a native PDFium build it was never compiled
/// against, which crashed the process outright once both libraries' code paths ran together (as
/// they do for every scanned PDF: <c>OcrStageHandler</c> uses both in the same job). Consolidating
/// on Docnet.Core for both jobs means only one native PDFium build is ever loaded, eliminating the
/// conflict at its root rather than pinning package versions and hoping the ABI stays compatible.
/// </summary>
public sealed class PdfPreviewGenerator : IDocumentPreviewGenerator
{
    /// <summary>Only the first few pages are rendered — a preview, not a full-document viewer (FR-043's bar).</summary>
    private const int MaxPreviewPages = 5;

    /// <summary>Renders at 2x each page's native point size (roughly 144 DPI) — legible for both a visual preview and as OCR input.</summary>
    private const double RenderScalingFactor = 2.0;

    public bool CanHandle(DocumentFileType fileType) => fileType == DocumentFileType.Pdf;

    public Task<IReadOnlyList<DocumentPreviewResult>> GenerateAsync(Stream content, DocumentFileType fileType, CancellationToken cancellationToken = default)
    {
        content.Position = 0;
        using var memoryStream = new MemoryStream();
        content.CopyTo(memoryStream);
        var pdfBytes = memoryStream.ToArray();

        using var docReader = DocLib.Instance.GetDocReader(pdfBytes, new PageDimensions(RenderScalingFactor));
        var pagesToRender = Math.Min(docReader.GetPageCount(), MaxPreviewPages);
        var results = new List<DocumentPreviewResult>(pagesToRender);

        for (var i = 0; i < pagesToRender; i++)
        {
            using var pageReader = docReader.GetPageReader(i);
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();
            var bgraBytes = pageReader.GetImage();

            using var image = Image.LoadPixelData<Bgra32>(bgraBytes, width, height);
            using var pageImageStream = new MemoryStream();
            image.SaveAsPng(pageImageStream, new PngEncoder());
            results.Add(new DocumentPreviewResult(DocumentPreviewType.PageImage, pageImageStream.ToArray(), i + 1));
        }

        return Task.FromResult<IReadOnlyList<DocumentPreviewResult>>(results);
    }
}
