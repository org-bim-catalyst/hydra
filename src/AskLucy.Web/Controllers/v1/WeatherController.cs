using AskLucy.Application.Weather;
using AskLucy.Application.Weather.Queries.GetCurrentWeather;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// The current-location weather lookup (specs/027-immersive-viewer-platform FR-009,
/// contracts/weather-api.md) backing the workspace viewer's weather widget. A pure
/// pass-through — no persistence (FR-012b).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1")]
[EnableRateLimiting("weather-endpoints")]
public sealed class WeatherController(ISender mediator) : ControllerBase
{
    [HttpGet("weather/current")]
    public async Task<ActionResult<WeatherSnapshotDto>> GetCurrent(
        [FromQuery] double latitude, [FromQuery] double longitude, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetCurrentWeatherQuery(latitude, longitude), cancellationToken);
        return Ok(result);
    }
}
