using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using Docnet.Core;
using Docnet.Core.Models;

namespace AskLucy.Infrastructure.Documents.Extraction;

/// <summary>
/// PDF text extraction (FR-022, research.md Decision 11 — replaces the untrustworthy
/// `UglyToad.PdfPig` package originally named in research.md Decision 5) via Docnet.Core
/// (PDFium-backed). Granularity is page-level only — Docnet.Core surfaces each page's raw text,
/// not font-size/style metadata, so true heading/table detection isn't possible from it the way
/// it is for OOXML formats; each page becomes one <c>"page"</c> structure element instead.
/// </summary>
public sealed class DocnetPdfTextExtractor : IDocumentTextExtractor
{
    public bool CanHandle(DocumentFileType fileType) => fileType == DocumentFileType.Pdf;

    public Task<DocumentTextExtractionResult> ExtractAsync(Stream content, DocumentFileType fileType, CancellationToken cancellationToken = default)
    {
        content.Position = 0;
        using var memoryStream = new MemoryStream();
        content.CopyTo(memoryStream);
        var pdfBytes = memoryStream.ToArray();

        using var docReader = DocLib.Instance.GetDocReader(pdfBytes, new PageDimensions(1080, 1920));
        var pageCount = docReader.GetPageCount();
        var elements = new List<DocumentStructureElement>(pageCount);

        for (var i = 0; i < pageCount; i++)
        {
            using var pageReader = docReader.GetPageReader(i);
            var pageText = pageReader.GetText();
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                elements.Add(new DocumentStructureElement("page", pageText, PageNumber: i + 1));
            }
        }

        var plainText = string.Join('\n', elements.Select(e => e.Text));
        return Task.FromResult(new DocumentTextExtractionResult(plainText, JsonSerializer.Serialize(elements), pageCount));
    }
}
