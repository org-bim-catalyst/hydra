using AskLucy.Application.SiteBoundaries;
using AskLucy.Application.SiteBoundaries.Queries.DrawSiteBoundaryDiagnostic;
using AskLucy.Application.SiteBoundaries.Queries.SegmentSiteBoundaryDiagnostic;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// One-off diagnostics (2026-09-06) comparing two ways to get an accurate boundary out of a
/// vision-capable model: <c>draw</c> asks it to draw the outline onto the map image (see
/// <c>IBoundaryDrawDiagnosticService</c>'s remarks — proved more accurate than coordinate-JSON
/// tracing on a first live test, but non-deterministic on a second one), and <c>segment</c> asks
/// for its native segmentation-mask output instead (see
/// <c>IBoundarySegmentationDiagnosticService</c>'s remarks — a real per-pixel classifier result,
/// no image generation involved). Admin-only: both burn a real AI provider call per request and
/// expose internal model wiring, not something to expose broadly. Neither is part of the shipped
/// boundary-resolution pipeline yet.
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

        return Ok(ToResponse(result));
    }

    [HttpGet("segment")]
    public async Task<IActionResult> Segment(
        [FromQuery] string siteName, [FromQuery] double lat, [FromQuery] double lon,
        [FromQuery] int radiusMeters, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new SegmentSiteBoundaryDiagnosticQuery(siteName, lat, lon, radiusMeters == 0 ? 150 : radiusMeters),
            cancellationToken);

        return Ok(ToResponse(result));
    }

    private static object ToResponse(BoundaryDrawDiagnosticResult result) => new
    {
        imageDataUrl = result.ImageBytes is { } bytes
            ? $"data:{result.ContentType ?? "image/png"};base64,{Convert.ToBase64String(bytes)}"
            : null,
        vertices = result.Vertices?.Select(v => new[] { v.Latitude, v.Longitude }),
        vertexSource = result.VertexSource,
        note = result.Note,
    };
}
