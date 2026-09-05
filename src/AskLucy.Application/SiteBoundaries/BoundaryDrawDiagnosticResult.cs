using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// The result of asking an AI vision model to draw a boundary directly onto a map image, rather
/// than report it as coordinates — a one-off diagnostic (2026-09-06), not part of the shipped
/// resolution pipeline. Only an actual image-generation model (Gemini's "Nano Banana" family) can
/// populate <see cref="ImageBytes"/>; a text/vision-only model assigned to the same capability can
/// only ever fill <see cref="Note"/> with whatever it said instead.
/// </summary>
/// <param name="Vertices">
/// The drawn outline, converted to real coordinates. Populated by the Infrastructure
/// implementation's deterministic pixel extraction (a connected-component + edge-trace over the
/// drawn red pixels) when it finds a plausible outline (<see cref="VertexSource"/> =
/// "pixel-extraction"); if that finds nothing plausible, by a second, separate AI call asking the
/// model to report the already-drawn line's own path instead of inferring a boundary
/// (<see cref="VertexSource"/> = "ai-line-read") — a mechanically easier task than the original
/// coordinate-tracing prompt this diagnostic exists to route around. Null if neither succeeded.
/// </param>
public sealed record BoundaryDrawDiagnosticResult(
    byte[]? ImageBytes, string? ContentType, string? Note,
    IReadOnlyList<GeoPoint>? Vertices = null, string? VertexSource = null);
