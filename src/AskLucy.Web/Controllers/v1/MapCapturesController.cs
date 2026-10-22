using System.Globalization;
using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.MapCaptures;
using AskLucy.Application.MapCaptures.Queries.CaptureMap;
using AskLucy.Web.Auth;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// specs/083-viewer-map-capture T026 — the server-to-server capture endpoint. It resolves a
/// location, renders one cropped PNG, and returns it; nothing is persisted (FR-014).
/// </summary>
[ApiController]
[Route("api/v1/map-captures")]
[Authorize(AuthenticationSchemes = ServiceApiKeyAuthenticationOptions.SchemeName)]
[EnableRateLimiting("map-capture-endpoints")]
[Produces("image/png")]
[ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
public sealed class MapCapturesController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Capture(
        [FromQuery] string? location,
        [FromQuery] double? latitude,
        [FromQuery] double? longitude,
        [FromQuery] string? mode,
        [FromQuery] string? overlays,
        [FromQuery(Name = "viewSizeMeters")] int? viewSizeMetres,
        [FromQuery] string? source,
        [FromQuery] int? crop,
        [FromQuery] int? zoom,
        CancellationToken cancellationToken)
    {
        var callerName = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

        MapImageResult result;
        try
        {
            result = await mediator.Send(
                new CaptureMapQuery(
                    location, latitude, longitude, viewSizeMetres, mode, overlays, callerName,
                    source, crop, zoom),
                cancellationToken);
        }
        catch (ValidationException validation)
        {
            // The platform's ValidationBehavior rejects a malformed request before the handler
            // runs, and the global middleware would answer "Validation failed". The capture
            // contract promises one stable 400 title, so it is set here instead (contracts/
            // map-capture-api.md#error-responses).
            return InvalidRequest(validation.Message);
        }

        if (!result.IsSuccess)
        {
            return ProblemFor(result.FailureReason!.Value, result.FailureDetail ?? "The capture could not be produced.");
        }

        Response.Headers["X-Map-Capture-Resolved-Location"] = HeaderValue(result.ResolvedLocation ?? string.Empty);
        Response.Headers["X-Map-Capture-Mode"] = HeaderValue(WireModeName(mode));
        Response.Headers["X-Map-Capture-Width"] = result.Width.ToString(CultureInfo.InvariantCulture);
        Response.Headers["X-Map-Capture-Height"] = result.Height.ToString(CultureInfo.InvariantCulture);

        return File(result.Png!, "image/png");
    }

    private ObjectResult ProblemFor(MapCaptureFailureReason reason, string detail)
    {
        var (status, title) = reason switch
        {
            MapCaptureFailureReason.LocationNotFound => (StatusCodes.Status404NotFound, "Location not found"),
            MapCaptureFailureReason.GeocodingUnavailable => (StatusCodes.Status502BadGateway, "Upstream provider unavailable"),
            MapCaptureFailureReason.ModeUnavailable => (StatusCodes.Status422UnprocessableEntity, "Capture not available"),
            MapCaptureFailureReason.OverlayUnavailable => (StatusCodes.Status422UnprocessableEntity, "Capture not available"),
            MapCaptureFailureReason.ZoomUnavailable => (StatusCodes.Status422UnprocessableEntity, "Capture not available"),
            MapCaptureFailureReason.ProviderUnavailable => (StatusCodes.Status502BadGateway, "Upstream provider unavailable"),
            MapCaptureFailureReason.CropTooSmall => (StatusCodes.Status400BadRequest, "Invalid capture request"),
            MapCaptureFailureReason.ImageTooLarge => (StatusCodes.Status422UnprocessableEntity, "Capture not available"),
            _ => (StatusCodes.Status500InternalServerError, "Capture failed"),
        };

        return ProblemWith(status, title, detail);
    }

    private ObjectResult InvalidRequest(string detail) =>
        ProblemWith(StatusCodes.Status400BadRequest, "Invalid capture request", detail);

    private ObjectResult ProblemWith(int status, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
        };

        if (HttpContext.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var correlationId))
        {
            problem.Extensions["traceId"] = correlationId;
        }

        return StatusCode(status, problem);
    }

    private static string WireModeName(string? mode) =>
        MapCaptureWireFormat.TryParseMode(mode, out var parsed)
            ? parsed switch
            {
                MapCaptureMode.Roadmap => "roadmap",
                MapCaptureMode.Hybrid => "hybrid",
                MapCaptureMode.BuildingsOnly => "buildings-only",
                _ => "satellite",
            }
            : "satellite";

    /// <summary>Header values must not carry CR/LF; a hostile location name must not be able to inject headers.</summary>
    private static string HeaderValue(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
