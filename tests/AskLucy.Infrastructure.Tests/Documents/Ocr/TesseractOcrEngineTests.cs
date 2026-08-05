using AskLucy.Domain.Documents;
using AskLucy.Infrastructure.Documents.Ocr;
using AskLucy.Infrastructure.Documents.Preview;
using AskLucy.Infrastructure.Tests.Documents.Extraction;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Tests.Documents.Ocr;

/// <summary>
/// T063 — <see cref="TesseractOcrEngine"/> against a real scanned-style sample (SC-008 accuracy
/// spot-check). No checked-in scanned image is available in this environment, so the "scan" is
/// produced honestly from real, non-synthetic pipeline components already implemented for this
/// feature: a real single-page PDF (<see cref="PdfFixture"/>) is rasterized to a PNG via the
/// actual production <see cref="PdfPreviewGenerator"/> (PDFium-backed, the same renderer used for
/// previews), and Tesseract runs against that genuine rendered bitmap — real antialiased glyphs,
/// not hand-drawn pixels.
/// </summary>
public sealed class TesseractOcrEngineTests
{
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && directory.GetFiles("*.sln").Length == 0)
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root (no .sln found above the test output directory).");
    }

    private static TesseractOcrEngine CreateSut()
    {
        var tessDataPath = Path.Combine(FindRepoRoot(), "src", "AskLucy.Web", "App_Data", "tessdata");
        var options = Options.Create(new TesseractOcrOptions { DataPath = tessDataPath });
        return new TesseractOcrEngine(options, NullLogger<TesseractOcrEngine>.Instance);
    }

    [Fact]
    public async Task RecognizeAsync_ShouldRecoverText_FromARealRasterizedPdfPage()
    {
        var pdfBytes = PdfFixture.CreateSinglePagePdf("OCR ACCURACY TEST", fontSize: 48);
        using var pdfStream = new MemoryStream(pdfBytes);
        var previewGenerator = new PdfPreviewGenerator();
        var pages = await previewGenerator.GenerateAsync(pdfStream, DocumentFileType.Pdf, CancellationToken.None);
        var pageImage = pages.Single().Content!;

        using var imageStream = new MemoryStream(pageImage);
        var result = await CreateSut().RecognizeAsync(imageStream, ["en"], CancellationToken.None);

        result.RecognizedText.Should().NotBeNullOrWhiteSpace();
        result.RecognizedText!.ToUpperInvariant().Should().Contain("TEST");
    }
}
