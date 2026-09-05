using AskLucy.Application.SiteBoundaries.Queries.DrawSiteBoundaryDiagnostic;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// One-off diagnostic (2026-09-06): asks whichever model is assigned to the BoundaryVision
/// capability to draw a site's boundary directly onto its map image, rather than report it as
/// coordinates — see <c>IBoundaryDrawDiagnosticService</c>'s remarks for why. Admin-only: this
/// burns a real AI provider call per request and exposes internal model wiring, not something to
/// expose broadly. Not part of the shipped boundary-resolution pipeline.
/// </summary>
[ApiController]
[Authorize(Policy = "AdministratorOrSuperUser")]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/boundary-diagnostics")]
public sealed class AdminBoundaryDiagnosticsController(ISender mediator) : ControllerBase
{
    [HttpGet("draw")]
    public async Task<IActionResult> Draw(
        [FromQuery] string siteName, [FromQuery] double lat, [FromQuery] double lon,
        [FromQuery] int radiusMeters, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new DrawSiteBoundaryDiagnosticQuery(siteName, lat, lon, radiusMeters == 0 ? 150 : radiusMeters),
            cancellationToken);

        return result.ImageBytes is { } bytes
            ? File(bytes, result.ContentType ?? "image/png")
            : Ok(new { note = result.Note ?? "No image returned." });
    }
}
