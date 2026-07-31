# Research: Cookie Consent & Privacy Management

**Feature**: [spec.md](./spec.md) | **Date**: 2026-07-30

All items below are design decisions needed to move from spec to data model/contracts;
none are open `NEEDS CLARIFICATION` markers — the stack is fixed by the existing Ask Lucy
solution (constitution §1/§3, CLAUDE.md), and the spec's three clarifications (strict
opt-in, English-only, blocking modal) are already resolved in spec.md's Clarifications
section. Research here is about *how* to implement those decisions with this codebase's
existing conventions.

## Topic 1: Where the consent concern lives (own module vs. extending User Management)

**Decision**: A new, narrowly-scoped `Consent` feature folder in each backend project
(`Domain/Consent/`, `Application/Consent/`, `Persistence/Configurations` +
`Repositories/UserCookieConsentRepository.cs`), plus a new `CookieConsentController`,
rather than folding it into the existing `Users`/`UsersController` feature.

**Rationale**: CLAUDE.md explicitly offers both options ("a Consent/Privacy module or
extension of the User Management Engine"). Constitution §2.II (SOLID/SRP: "a single,
nameable reason to change") and §2.VII (ISP: "narrow, client-specific interfaces, not fat
god interfaces") favor a dedicated module: consent has its own versioning/re-consent
lifecycle (policy-version comparisons, append-only history) that is conceptually distinct
from profile CRUD, and folding it into `IUserProfileRepository`/`UsersController` would
give that interface/controller a second reason to change.

**Alternatives considered**: Adding `GetMyCookieConsent`/`SaveMyCookieConsent` directly to
the existing `Users` folder and `UsersController` — rejected; profile fields
(name/avatar) and consent state (versioned, append-only, re-consent-triggering) have
different change cadences and validation rules, and `UsersController` already has ~10
actions (research agent survey) — adding 3 more, unrelated-to-profile actions would start
turning it into a god-controller (§2.II OCP/SRP concern).

## Topic 2: Modeling "current state" + "audit history" without a new audit-trail system

**Decision**: A single append-only table, `CookieConsentRecords`. Every accepted decision
(from the banner or from Settings) inserts a **new** row — rows are never updated or
hard-deleted in normal operation. "Current state" for a user is simply the row with the
latest `CreatedAtUtc` for that `UserId`; "history" (FR-016 — "what was this user's consent
state on date X") is the full set of rows ordered by `CreatedAtUtc`, no separate table
needed.

**Rationale**: Constitution §2.III (DRY/KISS/YAGNI) — a second "consent history" table
that mirrors "current consent" would duplicate the same columns and require keeping both
in sync on every write; a single append-only table serves both needs with one write path.
This also matches the precedent the research survey found in `specs/001-admin-dashboard`:
**no project-wide immutable audit-trail store exists yet** (tracked as an accepted,
documented gap via ADR, interim structured Serilog logging only). Building a bespoke
general audit system here would be out of this feature's scope; an append-only
domain-specific table satisfies FR-016 directly without that larger initiative.

**Alternatives considered**: One mutable "current consent" row per user plus a separate
`CookieConsentHistory` table written alongside every update — rejected as needless
duplication (§2.III) for no functional gain, since the append-only table alone already
answers both "what is it now" and "what was it on date X" with a single `ORDER BY
CreatedAtUtc` query.

## Topic 3: Policy version source — database-editable vs. configuration-bound

**Decision**: The "current cookie policy version" is a strongly-typed configuration value
(`CookiePolicyOptions.CurrentVersion` + `EffectiveAtUtc`), bound via `IOptions<T>` and
validated at startup (constitution §4 "Configuration is bound to strongly-typed
`IOptions<T>` classes, validated at startup"), read through a new Application-owned
`ICookiePolicyProvider` interface implemented in `AskLucy.Infrastructure`. Bumping the
version (e.g., when legal updates the policy or a new category ships) is a configuration
change + deploy, not a runtime admin action.

**Rationale**: Per constitution §4, configuration is "reserved for values that genuinely
vary by environment or tenant... not for structural decisions this constitution already
settles" — but the policy version *is* exactly the kind of externally-driven value (legal
sign-off, not code) `IOptions<T>` exists for, and re-consent (FR-007) only needs a simple
inequality check (`user's last PolicyVersion != current PolicyVersion`), not a
database-backed versioning workflow. Building an admin UI to edit policy versions is not
requested by spec.md and would be speculative (§2.III YAGNI).

**Alternatives considered**: A `CookiePolicyVersion` database table with an admin CRUD
screen — rejected as unrequested scope; policy-version changes are rare, deliberate,
legally-reviewed events, not a self-service admin workflow spec.md asks for.

## Topic 4: Cookie category metadata (names/descriptions) — API-served vs. frontend constant

**Decision**: The fixed category set (Essential, Functional, Analytics, Marketing) and
their user-facing descriptions are a small, static TypeScript constant shared by the
banner, the Settings panel, and the Privacy Page — not served from a new API endpoint.

**Rationale**: spec.md's Assumptions already fix this as "the initial fixed set... adding
a new category is treated as a policy-version change" — i.e., a code change (new
category needs new toggle UI and a new boolean column anyway), not independently-editable
content. Round-tripping four static strings through the API for three frontend surfaces
that already live in the same TypeScript codebase would be a speculative
API-for-API's-sake layer (§2.III YAGNI) with no consumer that needs it server-rendered.

**Alternatives considered**: A `GET /api/v1/cookie-categories` endpoint — rejected;
nothing in spec.md requires categories to change without a code deploy, and the frontend
already needs matching toggle components per category regardless of where the label text
lives.

## Topic 5: Enforcing "zero non-essential activity before consent" (FR-019) with no analytics integration yet

**Decision**: `useCookieConsent()` (TanStack Query) is the single source of truth for
granted categories on the client. This feature does not add any actual analytics/
marketing SDK — none currently exists in the codebase — but establishes the enforcement
point: any future analytics/marketing script loader MUST check
`consent.analytics`/`consent.marketing` from this hook before initializing, documented as
a binding convention in `docs/ARCHITECTURE.md` (tasks.md will include a documentation
task). `ConsentGate` (the component blocking the app pre-decision) guarantees no
authenticated page renders interactively before this hook has resolved.

**Rationale**: FR-019 is a policy about *when non-essential cookies may fire*, not a
request to build a new tracking integration. Satisfying it today means the platform has
nowhere non-essential activity currently fires before consent (true, since none exists)
and establishing the one required check point for whenever such an integration is added
— consistent with YAGNI (§2.III) while still making the constraint real and enforceable,
not just aspirational prose.

**Alternatives considered**: Building a placeholder analytics integration solely to
demonstrate gating — rejected; inventing infrastructure with no current consumer violates
§2.III and the Mission's "Never implement... premature optimization" instruction.

## Topic 6: Blocking-modal implementation (FR-020) reconciled with "banner appears on the main page"

**Decision**: `ConsentGate` wraps the existing authenticated app shell (inside
`ProtectedRoute`, per research survey — there is no separate app-shell/layout component
today). It renders the main page's content as normal, and — while a decision is required
— renders `CookieConsentBanner` as an MUI `Dialog` with `open` hardcoded `true` and a
no-op `onClose` (this MUI version removed the older `disableEscapeKeyDown` prop; leaving
`open` uncontrolled by `onClose` already achieves the same non-dismissible result — escape
key and backdrop click both fire `onClose`, which does nothing, so the dialog stays open)
on top of it. MUI `Dialog`'s modal backdrop already blocks
pointer/keyboard interaction with everything behind it, which satisfies "blocks
interaction with the rest of the app" (FR-020) while the banner is still visually
anchored to/over "the main page" (spec.md's literal wording), not a separate full-screen
route.

**Rationale**: Reuses the project's one existing modal primitive (MUI `Dialog`, already
used by `ConfirmDialog.tsx`) rather than inventing a bespoke non-dismissible banner
component from scratch — constitution §7 "composed from the existing MUI theme... before
a bespoke component is written."

**Alternatives considered**: A full-screen interstitial route (`/consent`) shown instead
of the main page — rejected; contradicts spec.md's explicit "banner appears in the main
page" framing (Input section) and would require a redirect dance that a same-page modal
avoids.

## Topic 7: Where the "Cookie Preferences" surface lives in Settings

**Decision**: A fourth MUI `Tab`/`TabPanel` ("Cookies") added to the existing
`SettingsPage.tsx` (which already has Security/Account/Data tabs), rendering a new
`CookiePreferencesPanel` component — not a new route/page.

**Rationale**: Directly matches the existing, established tab convention in
`SettingsPage.tsx` (constitution §2.VII Convention Over Configuration) and spec.md FR-011
("within the authenticated user's Settings area"), which does not require a distinct URL.

**Alternatives considered**: A new `/settings/cookies` route — rejected; every other
settings sub-section in this codebase is a tab, not a route, and there is no functional
need (deep-linking, etc.) that spec.md calls for beyond "reachable from Settings."

## Topic 8: Public Privacy Page — static content vs. server-rendered

**Decision**: `PrivacyPage.tsx` is a public React route (`/privacy`, outside
`ProtectedRoute`) with static English-only copy (research.md Topic 4), plus one live data
point fetched from the new anonymous `GET /api/v1/cookie-policy` endpoint: the current
policy version and effective date, so the page never shows a stale/hardcoded version
string.

**Rationale**: Satisfies FR-009/FR-010/FR-021 with the smallest surface: legal copy itself
doesn't need a backend round-trip (Topic 4), but the version/date is exactly the one value
that must stay in sync with the same source of truth the re-consent logic uses (Topic 3)
— serving it from the same `ICookiePolicyProvider` avoids two independent "current
version" definitions drifting apart.

**Alternatives considered**: Fully static page with a hardcoded version string —
rejected; would require a frontend code change in lockstep with every backend
`CookiePolicyOptions` config bump to avoid the Privacy Page showing an out-of-date
version, an easy-to-miss manual sync point.

## Topic 9: Global footer — new shared component

**Decision (as adjusted during implementation)**: A new `components/AppFooter.tsx`,
rendered by `AuthLayout` (covering login/register/etc. — pre-login discoverability) and
`PrivacyPage` itself, containing (at minimum) the Privacy link. The authenticated-app leg
of FR-010's "footer/global navigation" reachability is instead satisfied by a new "Privacy
Policy" `MenuItem` added to the existing `UserMenu` (already rendered in every
authenticated page's `AppBar`), not by a footer bar.

This revises the original pre-implementation decision below, which assumed `AppFooter`
would also render inside the authenticated chat shell.

**Original plan**: Render `AppFooter` in both the authenticated app shell and
`PrivacyPage`/other public pages.

**Why it changed**: `ChatPage.tsx` turned out to be a full-height (`100vh`) flex chat
layout — sidebar, message list, and a composer pinned to the bottom — with no footer
region; adding one would require a larger layout redesign out of this feature's scope.
`UserMenu` was already the authenticated app's de facto global-navigation surface
(Profile, Settings, Admin links), so a Privacy `MenuItem` there is the lower-risk,
already-established pattern for authenticated reachability, while `AppFooter` covers the
pages whose layout actually has room for a footer (`AuthLayout`, `PrivacyPage`).

**Rationale (still holds)**: The research survey confirmed no global footer/shell exists
today. Constitution §7 requires a new shared component to be "used by at least two
features, or justified as a foundational primitive" — `AppFooter` qualifies via
`AuthLayout` + `PrivacyPage` (two real, distinct usage sites), even though a third
originally-planned site (`ChatPage`) didn't pan out.

**Alternatives considered**: Putting the Privacy link only in `UserMenu` (authenticated
only), with no footer at all — rejected on its own; FR-010 also requires reachability
from *public* pages (Privacy itself, login/register) that have no `UserMenu`, so a shared
footer is still needed for those, even though `UserMenu` ended up covering the
authenticated leg. Redesigning `ChatPage`'s layout to make room for a footer bar —
rejected as disproportionate scope expansion for this feature.

## Topic 10: Rate limiting for the new endpoints (including the anonymous one)

**Decision**: A new named policy, `consent-endpoints`, added to `Program.cs` following
the exact `admin-endpoints`/`chat-endpoints` shape (fixed window, partitioned by
`User.Identity.Name ?? RemoteIpAddress ?? "anonymous"`), applied to all three
`CookieConsentController` actions — including the `[AllowAnonymous]` policy-version
endpoint, partitioned by IP for unauthenticated callers.

**Rationale**: The research survey found constitution §6 ("every public endpoint is
rate-limited") had previously been under-applied to new controllers in both prior
features (`specs/001-admin-dashboard`, `specs/002-chat-history-management` both had to
retroactively add a policy) — this feature applies one from the start rather than
repeating that gap, including for the anonymous endpoint, which is the more
abuse-exposed of the three (no auth required to call it).

**Alternatives considered**: Reusing `admin-endpoints`'s policy object — rejected only
because a dedicated named policy keeps the per-feature rate-limit shape independently
tunable later (e.g., if the Privacy Page's endpoint needs a stricter anonymous limit),
matching the one-named-policy-per-feature precedent already established.
