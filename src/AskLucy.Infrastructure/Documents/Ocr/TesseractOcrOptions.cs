namespace AskLucy.Infrastructure.Documents.Ocr;

/// <summary>Where the Tesseract <c>.traineddata</c> language files live (research.md Decision 3). Mirrors <c>LocalFileStorageOptions</c>'s relative-to-content-root convention.</summary>
public sealed class TesseractOcrOptions
{
    public const string SectionName = "TesseractOcr";

    public required string DataPath { get; init; }
}
