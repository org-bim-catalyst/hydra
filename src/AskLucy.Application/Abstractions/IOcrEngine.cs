namespace AskLucy.Application.Abstractions;

/// <summary><see cref="RecognizedText"/> is null (not an exception) when the engine ran successfully but found no recognizable text — a blank/illegible scan is a valid outcome, not a failure (spec.md Edge Cases).</summary>
public sealed record OcrResult(string? RecognizedText);

/// <summary>
/// OCR engine abstraction (FR-021, research.md Decision 3). The initial implementation
/// (<c>TesseractOcrEngine</c>) is self-hosted; a future cloud-OCR implementation is a drop-in
/// swap behind this interface with zero Application/Domain changes (Strategy pattern,
/// constitution §3 Infrastructure isolation).
/// </summary>
public interface IOcrEngine
{
    /// <summary>Recognizes text from a single rasterized page or image. <paramref name="languageHints"/> are ISO 639-1 codes the caller expects the content to contain, in priority order.</summary>
    Task<OcrResult> RecognizeAsync(Stream imageContent, IReadOnlyList<string> languageHints, CancellationToken cancellationToken = default);
}
