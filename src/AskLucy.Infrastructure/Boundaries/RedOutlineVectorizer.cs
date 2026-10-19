using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Images;

namespace AskLucy.Infrastructure.Boundaries;

/// <summary>
/// Turns a bright-red outline drawn by an image-generation model back into a vector ring — the
/// deterministic half of the "draw, don't describe" diagnostic pivot (2026-09-06). Asking a model
/// to draw directly on an image, its actual strength, proved far more accurate than asking it to
/// report normalised pixel coordinates for the same boundary on the first live test — but proved
/// non-deterministic on a second one (a small hallucinated extra notch), which is why this is a
/// diagnostic path being compared against Gemini's native segmentation-mask output
/// (<see cref="GeminiSegmentationDiagnosticService"/>), not yet the shipped approach.
/// </summary>
/// <remarks>
/// Builds a bright-red pixel mask, then hands it to <see cref="MaskContourVectorizer"/> — the same
/// connected-component + edge-trace + simplify pipeline a segmentation mask goes through, since
/// once something has produced "these pixels belong to the boundary," turning that into a ring is
/// the same problem regardless of source. Never throws (constitution §VIII) and returns
/// <see langword="null"/> — never a best-effort guess — whenever the image doesn't contain a
/// plausible outline.
/// </remarks>
internal static class RedOutlineVectorizer
{
    /// <summary>Loosely tuned against a "bright red" marker line; not sampled from a real Nano Banana output yet, so a miss here simply falls through to the AI-read fallback rather than mis-vectorizing.</summary>
    private const byte MinRed = 170;
    private const byte MaxGreenOrBlue = 100;
    private const int MinRedDominance = 60;

    public static IReadOnlyList<GeoPoint>? TryExtractRing(byte[] imageBytes, SatelliteImage bounds)
    {
        var image = RasterImage.Decode(imageBytes);
        var width = image.Width;
        var height = image.Height;

        var mask = new bool[width, height];
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var index = rowStart + x * 4;
                mask[x, y] = IsBoundaryRed(image.Rgba[index], image.Rgba[index + 1], image.Rgba[index + 2]);
            }
        }

        return MaskContourVectorizer.TryExtractRing(mask, width, height, bounds);
    }

    private static bool IsBoundaryRed(byte r, byte g, byte b) =>
        r >= MinRed
        && g <= MaxGreenOrBlue
        && b <= MaxGreenOrBlue
        && r - g >= MinRedDominance
        && r - b >= MinRedDominance;
}
