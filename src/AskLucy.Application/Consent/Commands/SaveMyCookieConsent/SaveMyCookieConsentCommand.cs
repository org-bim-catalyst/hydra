using MediatR;

namespace AskLucy.Application.Consent.Commands.SaveMyCookieConsent;

/// <summary>
/// Records a new consent decision (specs/004-cookie-consent-privacy). One endpoint covers
/// Accept All / Reject Non-Essential / Customize — the frontend translates the button
/// pressed into this same request shape (contracts/cookie-consent-api.md). Essential is
/// never accepted here — it is not a client-controlled value (spec.md FR-004).
/// </summary>
/// <remarks>
/// Fields are <c>bool?</c>, not <c>bool</c>: a non-nullable <c>bool</c> can't distinguish an
/// omitted JSON field from an explicit <c>false</c> (both bind to the type's default), so a
/// "required field" validator on a plain <c>bool</c> would never actually fire. Nullability
/// makes "missing" a real, validatable state (see <see cref="SaveMyCookieConsentCommandValidator"/>).
/// </remarks>
public sealed record SaveMyCookieConsentCommand(bool? Functional, bool? Analytics, bool? Marketing) : IRequest<CookieConsentStatusDto>;
