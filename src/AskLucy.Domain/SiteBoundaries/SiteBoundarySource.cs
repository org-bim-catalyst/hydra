namespace AskLucy.Domain.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — where a resolved boundary's shape came from. Only
/// <see cref="OsmBoundary"/> and <see cref="ManualFallback"/> are produced by any v1 code path;
/// the rest are reserved so this enum doesn't need a breaking change when a future provider
/// (government cadastral data, an AI-vision critique, an uploaded file) is added (OCP).
/// </summary>
public enum SiteBoundarySource
{
    OsmBoundary,
    GovernmentCadastral,
    AiInterpretation,
    UploadedBoundary,
    ManualFallback,
}
