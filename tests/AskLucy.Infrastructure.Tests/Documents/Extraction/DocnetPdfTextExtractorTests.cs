using AskLucy.Domain.Documents;
using AskLucy.Infrastructure.Documents.Extraction;
using FluentAssertions;

namespace AskLucy.Infrastructure.Tests.Documents.Extraction;

/// <summary>T063 — <see cref="DocnetPdfTextExtractor"/> against a real, minimal single-page PDF (FR-022).</summary>
public sealed class DocnetPdfTextExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ShouldRecoverTheTextLayer_FromARealPdf()
    {
        var pdfBytes = PdfFixture.CreateSinglePagePdf("Hello World PDF Extraction Test");
        using var pdfStream = new MemoryStream(pdfBytes);
        var extractor = new DocnetPdfTextExtractor();

        var result = await extractor.ExtractAsync(pdfStream, DocumentFileType.Pdf, CancellationToken.None);

        result.PlainText.Should().Contain("Hello World PDF Extraction Test");
        result.PageCount.Should().Be(1);
    }
}
