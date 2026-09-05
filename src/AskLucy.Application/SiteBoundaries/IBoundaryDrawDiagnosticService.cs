using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// Diagnostic-only counterpart to <see cref="IBoundaryVisionAnalyzer"/>: instead of asking the
/// model to report a boundary as normalised coordinates, this asks it to draw the boundary
/// directly onto the map image and hand back the edited image — the same experiment the user ran
/// by hand against ChatGPT. Whichever provider/model is assigned to <c>AiCapability.BoundaryVision</c>
/// is reused (no separate admin setting), so switching that assignment is how a caller picks which
/// model this test runs against. Never throws (constitution §VIII): every failure path returns a
/// <see cref="BoundaryDrawDiagnosticResult"/> with a null <c>ImageBytes</c> and an explanatory
/// <c>Note</c>.
/// </summary>
public interface IBoundaryDrawDiagnosticService
{
    Task<BoundaryDrawDiagnosticResult> DrawAsync(
        SatelliteImage image, string siteName, CancellationToken cancellationToken = default);
}
