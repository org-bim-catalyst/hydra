# API Contract: Cookie Consent & Privacy Management

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New resource, `src/AskLucy.Web/Controllers/v1/CookieConsentController.cs`
(`[ApiController] [Route("api/v1")] [Authorize]` at class level — research.md Topic 1).
The route template itself (`api/v1`, not a narrower resource-scoped prefix) is broader
than `UsersController`'s `api/v1/users`, because this controller spans two distinct
resources (`users/me/cookie-consent` and the top-level `cookie-policy`) that don't share
a common parent path; what *is* reused from `UsersController` is its `[Authorize]`-by-
default class with a per-action `[AllowAnonymous]` override — the same pattern
`UsersController.GetAvatar` already uses for its one public endpoint. All error bodies are
RFC 7807 Problem Details
(constitution §6); no ad hoc `{ "error": "..." }` shapes. All three actions are subject to
the new `consent-endpoints` rate-limit policy (research.md Topic 10).

## Get my current consent status

`GET /api/v1/users/me/cookie-consent`

**Auth**: `[Authorize]` — resolves the acting user via `ICurrentUserAccessor.UserId`,
never a client-supplied id (mirrors `GetMyProfileQueryHandler`).

Response: `200 OK`, `CookieConsentResponse`:

```json
{
  "hasConsented": true,
  "requiresReconsent": false,
  "policyVersion": "2026-07-30.1",
  "currentPolicyVersion": "2026-07-30.1",
  "essential": true,
  "functional": true,
  "analytics": false,
  "marketing": false,
  "lastUpdatedAtUtc": "2026-07-30T14:02:11Z"
}
```

A first-time user (no `CookieConsentRecord` exists) gets `200 OK` with `hasConsented:
false`, `requiresReconsent: true`, `policyVersion: null`, `functional`/`analytics`/
`marketing: false`, `lastUpdatedAtUtc: null` — never a `404`, since "no decision yet" is
an expected, valid state (spec.md User Story 1), not an error.

`requiresReconsent` is the single field `ConsentGate` reads to decide whether to render
the blocking banner (spec.md FR-001/FR-006/FR-007).

## Save my consent decision

`PUT /api/v1/users/me/cookie-consent`

**Auth**: `[Authorize]`. Request: `SaveCookieConsentRequest`:

```json
{ "functional": true, "analytics": false, "marketing": false }
```

All three fields are typed `bool?` (nullable), not `bool`, in both the request contract and
the underlying `SaveMyCookieConsentCommand` — a non-nullable `bool` cannot distinguish an
omitted JSON field from an explicit `false` (both bind to `false`), so a "required field"
validator on a plain `bool` would never actually fire. Nullability makes "missing" a real,
validatable state (`FluentValidation`'s `NotNull()`), and the handler unwraps to non-null
`bool` only after validation guarantees it.

- `Essential` is never accepted in the request body — it is not a client-controlled value
  (spec.md FR-004). `FluentValidation` rejects a request body that includes an
  `essential`/`Essential` field with a value other than absent (`400`) is **not** required
  since the DTO simply has no such property to bind — overposting is impossible by
  construction (mirrors the anti-overposting pattern in `Contracts/UserContracts.cs`).
- One endpoint handles all three banner actions: "Accept All" sends
  `{ functional: true, analytics: true, marketing: true }`; "Reject Non-Essential" sends
  `{ functional: false, analytics: false, marketing: false }`; "Customize" sends whatever
  per-category combination the user picked. There is no separate "accept all"/"reject all"
  action — the frontend translates the button pressed into the same request shape
  (research.md Topic 6/no-speculative-endpoints reasoning).

Handler inserts a **new** `CookieConsentRecord` (never updates an existing one,
data-model.md) stamped with `ICookiePolicyProvider`'s current version at save time, so a
save always resolves `requiresReconsent` to `false` for the version in effect at that
moment.

Response: `200 OK`, the same `CookieConsentResponse` shape as the GET above, reflecting
the just-saved state.

**Validation** (`SaveMyCookieConsentCommandValidator`, FluentValidation): no field may be
`null` (all three booleans required in the request; a `PUT` is a full-replace, not a
partial patch) — `400` Problem Details on failure.

## Get the current cookie policy (public)

`GET /api/v1/cookie-policy`

**Auth**: `[AllowAnonymous]` — reachable from the public Privacy Page without a session
(spec.md FR-009, research.md Topic 8).

Response: `200 OK`, `CookiePolicyResponse`:

```json
{ "version": "2026-07-30.1", "effectiveAtUtc": "2026-07-30T00:00:00Z" }
```

Backed by the same `ICookiePolicyProvider` the authenticated endpoints use — one
definition of "current version," never two independent sources that could drift
(research.md Topic 3/Topic 8).

## Security & error shape (applies to every endpoint above)

- `401` if unauthenticated on either `me` endpoint (constitution §8; the policy endpoint
  never requires auth).
- `400` Problem Details on validation failure (missing/invalid fields in the `PUT` body).
- Every consent save is logged via structured Serilog as a security-relevant event
  (constitution §8 "log security events"; research.md Topic 1's interim-logging
  precedent) — user id, prior/new category values, and policy version, never raw request
  bodies beyond those fields.
- No `404` path exists for the `me` endpoints — "no consent recorded yet" is a `200` with
  `hasConsented: false`, not a missing-resource error (see the GET response above).
