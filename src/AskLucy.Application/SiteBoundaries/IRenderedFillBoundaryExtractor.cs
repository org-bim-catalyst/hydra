using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution §9.8 (2026-09-06) — traces a boundary deterministically from
/// Google's own map rendering, with no AI model involved at all. Google Static Maps' <c>style</c>
/// parameter can force a named feature category's fill to an exact, known colour; that colour is
/// then found by pixel thresholding and its outline traced by exact pixel-grid edge tracing, the
/// same class of approach validated for <c>IBoundaryDrawDiagnosticService</c>'s drawn-outline
/// diagnostic — except here there is no generative model in the loop to hallucinate, and no
/// coordinate-JSON output for a model to get imprecise about. Validated against Al Safa Park 2's 6
/// independently hand-picked ground-truth vertices: every one matched within 0.6 m, beating every
/// AI-vision approach tried (coordinate tracing, drawing, native segmentation) by an order of
/// magnitude, for free and without per-request AI cost or latency.
/// </summary>
/// <remarks>
/// Scoped narrowly on purpose: only validated for park-like features so far
/// (<c>googleMapsFeatureType</c> is a caller-chosen Static Maps style feature category, e.g.
/// <c>"poi.park"</c>), so <see cref="BoundaryResolutionService"/> only calls this for candidates
/// whose OSM tags indicate a park; everything else still goes through the AI vision flow
/// unchanged. Never throws (constitution §VIII): any failure — no API key, a request failure, no
/// plausible outline found in the resulting image — returns <see langword="null"/>, exactly like
/// <see cref="ISatelliteImageProvider"/>'s own contract, so the caller degrades to its existing
/// fallback rather than losing the turn.
/// </remarks>
public interface IRenderedFillBoundaryExtractor
{
    Task<IReadOnlyList<GeoPoint>?> TryExtractAsync(
        GeoPoint center, int radiusMeters, string googleMapsFeatureType, CancellationToken cancellationToken = default);
}
