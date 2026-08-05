using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tesseract;

namespace AskLucy.Infrastructure.Documents.Ocr;

internal static partial class TesseractOcrEngineLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "OCR failed for a page — treating as no recognizable text")]
    public static partial void RecognitionFailed(ILogger logger, Exception exception);
}

/// <summary>
/// Self-hosted OCR (FR-021, research.md Decision 3) via the Tesseract 5 engine. A fresh
/// <see cref="TesseractEngine"/> is constructed per call rather than pooled/reused — the
/// wrapper is not safe for concurrent use from multiple threads on one instance, and this
/// engine may be invoked concurrently across different documents' background jobs.
/// </summary>
public sealed partial class TesseractOcrEngine(IOptions<TesseractOcrOptions> options, ILogger<TesseractOcrEngine> logger) : IOcrEngine
{
    private static readonly Dictionary<string, string> IsoToTesseractLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "eng",
        ["ar"] = "ara",
        ["fr"] = "fra",
        ["de"] = "deu",
        ["es"] = "spa",
        ["it"] = "ita",
        ["pt"] = "por",
        ["zh"] = "chi_sim",
        ["ja"] = "jpn",
        ["ko"] = "kor",
        ["ru"] = "rus",
    };

    public Task<OcrResult> RecognizeAsync(Stream imageContent, IReadOnlyList<string> languageHints, CancellationToken cancellationToken = default)
    {
        var language = ResolveAvailableLanguage(languageHints);

        try
        {
            using var memoryStream = new MemoryStream();
            imageContent.CopyTo(memoryStream);
            var imageBytes = memoryStream.ToArray();

            using var engine = new TesseractEngine(options.Value.DataPath, language, EngineMode.Default);
            using var pix = Pix.LoadFromMemory(imageBytes);
            using var page = engine.Process(pix);

            var text = page.GetText();
            return Task.FromResult(new OcrResult(string.IsNullOrWhiteSpace(text) ? null : text.Trim()));
        }
        catch (Exception ex) when (ex is TesseractException or IOException)
        {
            TesseractOcrEngineLog.RecognitionFailed(logger, ex);
            return Task.FromResult(new OcrResult(null));
        }
    }

    /// <summary>Falls back to whichever trained-data file is actually present (defaulting to "eng") rather than failing outright when a hinted language's pack isn't installed — the OCR/language-coverage assumption (spec.md) is that languages are added incrementally.</summary>
    private string ResolveAvailableLanguage(IReadOnlyList<string> languageHints)
    {
        foreach (var hint in languageHints)
        {
            if (IsoToTesseractLanguage.TryGetValue(hint, out var tesseractCode)
                && File.Exists(Path.Combine(options.Value.DataPath, $"{tesseractCode}.traineddata")))
            {
                return tesseractCode;
            }
        }

        return "eng";
    }
}
