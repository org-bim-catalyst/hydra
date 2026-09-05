namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// The result of asking an AI vision model to draw a boundary directly onto a map image, rather
/// than report it as coordinates — a one-off diagnostic (2026-09-06), not part of the shipped
/// resolution pipeline. Only an actual image-generation model (Gemini's "Nano Banana" family) can
/// populate <see cref="ImageBytes"/>; a text/vision-only model assigned to the same capability can
/// only ever fill <see cref="Note"/> with whatever it said instead.
/// </summary>
public sealed record BoundaryDrawDiagnosticResult(byte[]? ImageBytes, string? ContentType, string? Note);
