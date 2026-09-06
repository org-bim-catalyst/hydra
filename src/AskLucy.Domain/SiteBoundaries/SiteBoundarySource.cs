namespace AskLucy.Domain.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — where a resolved boundary's shape came from.
/// <see cref="OsmBoundary"/>, <see cref="AiInterpretation"/>, <see cref="ManualFallback"/>, and
/// <see cref="RenderedMapExtraction"/> (2026-09-06) are produced by shipped code paths; the rest
/// are reserved so this enum doesn't need a breaking change when a future provider (government
/// cadastral data, an uploaded file) is added (OCP).
/// </summary>
public enum SiteBoundarySource
{
    OsmBoundary,
    GovernmentCadastral,
    AiInterpretation,
    UploadedBoundary,
    ManualFallback,

    /// <summary>
    /// Traced deterministically from Google's own map rendering: the target feature's fill is
    /// forced to an exact, known colour via the Static Maps <c>style</c> parameter, then extracted
    /// by pixel thresholding and contour tracing — no AI model involved. Validated against Al Safa
    /// Park 2 (all 6 independently hand-picked ground-truth vertices matched within 0.6 m), after
    /// coordinate-JSON tracing, drawing, and native segmentation — three separate AI-vision
    /// approaches — each either under-traced real detail or proved non-deterministic across
    /// repeated calls. See docs/LOCATION_TO_BOUNDARY_END_TO_END.md §9.8.
    /// </summary>
    RenderedMapExtraction,
}
