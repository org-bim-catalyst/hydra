using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// Second diagnostic path (2026-09-06), alongside <see cref="IBoundaryDrawDiagnosticService"/>:
/// asks whichever model is assigned to <c>AiCapability.BoundaryVision</c> for its native
/// segmentation-mask output on the site instead of asking it to draw on the image. A segmentation
/// mask is a genuine per-pixel classifier result — no image generation involved — which is the
/// "vision model + segmentation" combination recommended as more reliable than image-generation
/// tracing for this exact task. The Infrastructure implementation vectorizes that mask
/// deterministically, the same way the drawn-outline diagnostic vectorizes a drawn red line.
/// Reuses <see cref="BoundaryDrawDiagnosticResult"/>'s shape: an
/// optional rendered image (the extracted outline drawn back onto the source map, for visual
/// comparison against the draw diagnostic), the derived <c>Vertices</c>, and an explanatory
/// <c>Note</c> when nothing plausible came back.
/// </summary>
public interface IBoundarySegmentationDiagnosticService
{
    Task<BoundaryDrawDiagnosticResult> SegmentAsync(
        SatelliteImage image, string siteName, CancellationToken cancellationToken = default);
}
