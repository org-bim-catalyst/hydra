using MediatR;

namespace AskLucy.Application.Consent.Queries.GetCookiePolicy;

/// <summary>Public/anonymous — no current-user dependency (specs/004-cookie-consent-privacy).</summary>
public sealed record GetCookiePolicyQuery : IRequest<CookiePolicyDto>;
