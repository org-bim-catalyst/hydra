using AskLucy.Application.Consent.Commands.SaveMyCookieConsent;
using AskLucy.Application.Consent.Queries.GetCookiePolicy;
using AskLucy.Application.Consent.Queries.GetMyCookieConsent;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>
/// Cookie consent, preferences, and the public policy-version endpoint
/// (specs/004-cookie-consent-privacy, contracts/cookie-consent-api.md). The route template
/// is broader than a single resource prefix (`api/v1`, not `api/v1/users`) because this
/// controller spans two distinct resources — `users/me/cookie-consent` and the top-level
/// `cookie-policy` — that don't share a common parent path.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1")]
[EnableRateLimiting("consent-endpoints")]
public sealed class CookieConsentController(ISender mediator) : ControllerBase
{
    [HttpGet("users/me/cookie-consent")]
    public async Task<ActionResult<CookieConsentResponse>> GetMine(CancellationToken cancellationToken)
    {
        var status = await mediator.Send(new GetMyCookieConsentQuery(), cancellationToken);
        return Ok(ToResponse(status));
    }

    [HttpPut("users/me/cookie-consent")]
    public async Task<ActionResult<CookieConsentResponse>> SaveMine(SaveCookieConsentRequest request, CancellationToken cancellationToken)
    {
        var status = await mediator.Send(
            new SaveMyCookieConsentCommand(request.Functional, request.Analytics, request.Marketing),
            cancellationToken);
        return Ok(ToResponse(status));
    }

    /// <summary>Public — reachable pre-login for the Privacy Page (spec.md FR-009).</summary>
    [HttpGet("cookie-policy")]
    [AllowAnonymous]
    public async Task<ActionResult<CookiePolicyResponse>> GetPolicy(CancellationToken cancellationToken)
    {
        var policy = await mediator.Send(new GetCookiePolicyQuery(), cancellationToken);
        return Ok(new CookiePolicyResponse(policy.Version, policy.EffectiveAtUtc));
    }

    private static CookieConsentResponse ToResponse(Application.Consent.CookieConsentStatusDto status) => new(
        status.HasConsented,
        status.RequiresReconsent,
        status.PolicyVersion,
        status.CurrentPolicyVersion,
        status.Essential,
        status.Functional,
        status.Analytics,
        status.Marketing,
        status.LastUpdatedAtUtc);
}
