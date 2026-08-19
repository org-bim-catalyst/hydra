using AskLucy.Application.Analytics.Commands.RecordFunnelEvent;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// Consent-gated funnel/CTA analytics event recording for the public landing/auth pages
/// (specs/023-flumeria-landing-experience, contracts/analytics-funnel-events-api.md).
/// </summary>
[ApiController]
[Route("api/v1/analytics")]
[EnableRateLimiting("analytics-endpoints")]
public sealed class AnalyticsController(ISender mediator) : ControllerBase
{
    /// <summary>Always anonymous — called before any session exists (FR-021).</summary>
    [HttpPost("funnel-events")]
    [AllowAnonymous]
    public async Task<IActionResult> RecordFunnelEvent(RecordFunnelEventRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(
            new RecordFunnelEventCommand(request.EventType, request.CtaId, request.FunnelType, request.SessionId, request.OccurredAtUtc),
            cancellationToken);
        return Accepted();
    }
}
