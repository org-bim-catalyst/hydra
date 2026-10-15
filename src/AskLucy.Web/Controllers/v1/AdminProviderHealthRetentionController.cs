using AskLucy.Domain.Authorization;
using AskLucy.Infrastructure.Ai;
using AskLucy.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// Read-only view of how long provider health checks are kept (<c>ProviderHealthCheck:RetentionDays</c>). The value comes from
/// configuration; changing it stays a deployment change until an editable setting is approved.
/// </summary>
[ApiController]
[EnableRateLimiting("admin-endpoints")]
[Route("api/v1/admin/provider-health/retention")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminProviderHealthRetentionController(IOptions<ProviderHealthCheckOptions> options) : ControllerBase
{
    [HttpGet]
    [RequirePermission(AdminPermissionCatalog.OperationalFailuresView)]
    [ProducesResponseType<ProviderHealthRetentionDto>(StatusCodes.Status200OK)]
    public ActionResult<ProviderHealthRetentionDto> Get() =>
        Ok(new ProviderHealthRetentionDto(options.Value.RetentionDays, options.Value.Interval.TotalMinutes));
}

public sealed record ProviderHealthRetentionDto(int RetentionDays, double IntervalMinutes);
