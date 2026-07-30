using MediatR;

namespace AskLucy.Application.Consent.Queries.GetMyCookieConsent;

public sealed record GetMyCookieConsentQuery : IRequest<CookieConsentStatusDto>;
