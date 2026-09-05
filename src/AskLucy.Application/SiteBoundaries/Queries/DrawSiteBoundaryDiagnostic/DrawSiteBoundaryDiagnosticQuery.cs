using AskLucy.Application.SiteBoundaries;
using MediatR;

namespace AskLucy.Application.SiteBoundaries.Queries.DrawSiteBoundaryDiagnostic;

/// <summary>
/// One-off diagnostic (2026-09-06) — see <see cref="IBoundaryDrawDiagnosticService"/>. Bypasses
/// OSM candidate search entirely: <paramref name="RadiusMeters"/> is given directly rather than
/// derived from a candidate's mapped ring, since this exists specifically to test tracing quality
/// independent of anything OSM-derived.
/// </summary>
public sealed record DrawSiteBoundaryDiagnosticQuery(
    string SiteName, double Latitude, double Longitude, int RadiusMeters) : IRequest<BoundaryDrawDiagnosticResult>;
