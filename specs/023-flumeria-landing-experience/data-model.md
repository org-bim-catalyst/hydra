# Data Model: Flumeria Public Landing Experience

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

**No new Domain entities, no new database table, no EF Core migration** — this feature is a front-end presentation/routing change plus two small, deliberately non-persisted additions (research.md Topics 2 and 4). No changes to `ApplicationUser`, `CookieConsentRecord`, or any other existing aggregate.

## PublicConsentState (client-side only — first-party cookie, not a server entity)

Governs whether the new anonymous public pages (landing + auth-flow pages) may fire funnel/CTA analytics events. Structurally mirrors the authenticated `CookieConsentStatusDto` (specs/004) category shape for visual/UX consistency, but is read and written entirely client-side via `usePublicCookieConsent()` — there is no backend endpoint for it.

| Field | Type | Notes |
|---|---|---|
| `policyVersion` | `string` | The cookie/privacy policy version the decision was made under; reuses the same version string the authenticated flow uses (`ICookiePolicyProvider`'s current version, obtained via the existing `[AllowAnonymous]` `GetCookiePolicyQuery` endpoint that already backs `/privacy`) |
| `essential` | `bool` | Always `true` (constant) — never toggleable, mirrors `COOKIE_CATEGORIES` (`essential.locked = true`) |
| `functional` | `bool` | From `COOKIE_CATEGORIES` taxonomy (`src/AskLucy.Web/ClientApp/src/features/consent/cookieCategories.ts`) |
| `analytics` | `bool` | Gates whether `useFunnelAnalytics()` may fire any event (FR-021) |
| `marketing` | `bool` | From `COOKIE_CATEGORIES` taxonomy; not used by this feature's own logic but captured for consistency and to avoid re-prompting once the visitor later authenticates and the authenticated flow's own preferences panel is available |
| `decidedAtUtc` | `string` (ISO 8601) | When the decision was made, client clock |

**Storage**: a single first-party cookie (e.g. `flumeria_public_consent`), JSON-encoded, `SameSite=Lax`, no expiry beyond a reasonable long-lived default (mirrors typical consent-cookie practice). **Not** written to any server table — this is a deliberate, documented limitation (research.md Topic 2): anonymous consent evidence is not part of specs/004's audit trail. If the visitor authenticates, the existing authenticated `ConsentGate`/`CookieConsentRecord` flow takes over immediately for all subsequent sessions, unaffected by this cookie.

**Validation rules**: `essential` is never read from the cookie (always treated as `true`); a missing or malformed cookie is treated identically to "no decision yet" (banner shown, no analytics fired) — never as an error state, consistent with FR-017's no-silent-failure standard being reserved for actual failures, not an expected "first visit" condition.

**Lifecycle**:

```text
(no cookie) ──visitor makes a banner choice──> flumeria_public_consent cookie written
flumeria_public_consent (analytics=false) ──visitor changes mind (banner still offered)──> cookie overwritten
flumeria_public_consent (analytics=true) ──visitor signs up/in──> authenticated ConsentGate takes over;
    this cookie is no longer consulted (existing per-user CookieConsentRecord flow governs from here on)
```

## FunnelAnalyticsEvent (structured log event shape — not persisted to a database)

Recorded via `ILogger<T>.LogInformation` with named properties inside `RecordFunnelEventCommandHandler` (`Application/Analytics/Commands/RecordFunnelEvent/`), shipped to the existing centralized Serilog sink (constitution §14). Not an EF Core entity; no table, no migration, no repository.

| Field | Type | Notes |
|---|---|---|
| `EventType` | enum: `CtaClicked` \| `FunnelCompleted` | Closed set, validated by FluentValidation — rejects any other value (§8 threat-model: prevents log-forging via arbitrary strings) |
| `CtaId` | enum: `SignIn` \| `SignUp` \| `TryPlatform` | Required when `EventType = CtaClicked`; otherwise absent |
| `FunnelType` | enum: `SignUp` \| `SignIn` | Required when `EventType = FunnelCompleted`; otherwise absent. `SignUp` means the registration form was submitted successfully (confirmation-pending state reached) — registration does not issue a session, so it never means "reached the workspace" (spec.md FR-008, Clarifications). `SignIn` means the visitor actually reached the workspace. |
| `SessionId` | `Guid` | Client-generated, ephemeral (regenerated per browser session via `sessionStorage`), **not** derived from or linked to `UserId`/email — carries no PII |
| `OccurredAtUtc` | `DateTime` (UTC) | Client-supplied event timestamp, used for funnel-duration measurement (SC-001/SC-002); server also stamps its own receipt time in the log entry for cross-checking clock skew |

**Validation rules** (Application layer, `RecordFunnelEventCommandValidator`):
- `EventType` must be one of the two enum values.
- Exactly one of `CtaId` / `FunnelType` must be present, matching `EventType`.
- `SessionId` required, must be a valid GUID.
- `OccurredAtUtc` required, must not be in the future beyond a small clock-skew tolerance, and not older than a bounded recent window (rejects replay/garbage timestamps).

**Lifecycle**: emitted once per CTA click and once per funnel completion; no update, no delete, no query surface — write-only telemetry, consistent with research.md Topic 4's reasoning for not modeling it as a persisted aggregate.

## Entity Relationship Summary

```text
No new entities relate to ApplicationUser or any existing aggregate.
PublicConsentState  — client-owned, cookie-scoped to browser, not linked to any entity.
FunnelAnalyticsEvent — log-only, SessionId is an ephemeral client-generated correlation value, not a foreign key.
```

## Index

N/A — no new table.
