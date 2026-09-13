using AskLucy.Application.Buildings;
using AskLucy.Application.Buildings.Queries.GetSiteBuildings;
using AskLucy.Infrastructure.Buildings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/052-solar-analysis contracts/building-footprints-endpoint.md — GET /api/v1/site-buildings.
/// Retrieves building footprints around a point through the platform (spec Clarification 3,
/// research D4). Authenticated and rate-limited, mirroring <see cref="WeatherController"/>'s
/// pattern exactly (§6). <see cref="BuildingProviderUnavailableException"/> is mapped to
/// <c>503 Service Unavailable</c> Problem Details by <c>ProblemDetailsMiddleware</c> — this
/// controller performs no try/catch of its own (constitution §2.VIII: never a swallowed failure,
/// and the cross-cutting middleware is the one correct place for this translation).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1")]
[EnableRateLimiting("buildings-endpoints")]
public sealed class SiteBuildingsController(ISender mediator, IOptions<BuildingRetrievalOptions> options) : ControllerBase
{
    [HttpGet("site-buildings")]
    public async Task<ActionResult<BuildingFootprintResult>> GetSiteBuildings(
        [FromQuery] double latitude, [FromQuery] double longitude, [FromQuery] int? radiusMetres,
        CancellationToken cancellationToken)
    {
        var effectiveRadius = radiusMetres ?? options.Value.DefaultRadiusMetres;
        var result = await mediator.Send(new GetSiteBuildingsQuery(latitude, longitude, effectiveRadius), cancellationToken);
        return Ok(result);
    }
}
