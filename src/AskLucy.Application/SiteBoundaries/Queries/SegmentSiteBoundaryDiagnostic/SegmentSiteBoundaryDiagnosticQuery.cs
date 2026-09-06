using AskLucy.Application.SiteBoundaries;
using MediatR;

namespace AskLucy.Application.SiteBoundaries.Queries.SegmentSiteBoundaryDiagnostic;

/// <summary>
/// One-off diagnostic (2026-09-06) — see <see cref="IBoundarySegmentationDiagnosticService"/>.
/// Same shape as <c>DrawSiteBoundaryDiagnosticQuery</c>, deliberately: this is the same test with
/// one different variable (a native segmentation mask instead of a generated drawing), so the two
/// need to be directly comparable.
/// </summary>
public sealed record SegmentSiteBoundaryDiagnosticQuery(
    string SiteName, double Latitude, double Longitude, int RadiusMeters) : IRequest<BoundaryDrawDiagnosticResult>;
