# ADR 0013: Hangfire Dashboard Access via a Purpose-Scoped Cookie, and Its Theming Gap

**Status:** Accepted

**Date:** 2026-09-19

**Feature:** [specs/060-hangfire-dashboard-access](../../specs/060-hangfire-dashboard-access/spec.md)

## Context

The Hangfire dashboard (`/hangfire`) already existed, gated by `HangfireDashboardAuthorizationFilter`,
but was reachable only by administrators who knew the URL and could attach credentials themselves.
The admin panel is a bearer-token SPA; Hangfire's dashboard is a separate, server-rendered surface
that must be opened as a genuine new top-level tab (not an iframe, to avoid CSP/`X-Frame-Options`
complexity and iframe-adjacent popup fragility). A bearer token cannot ride along with `window.open`
navigation, so some other credential has to authenticate that tab.

Separately, the dashboard was expected to visually match Ask Lucy rather than showing Hangfire's
stock Bootstrap 3 look, including following the admin panel's own light/dark toggle.

## Decision

**Authentication**: `POST /api/v1/admin/hangfire/session` (Administrator/Super User policy, rate
limited) mints a token via the existing `ITokenService` — same signing key and validation the
default JWT Bearer scheme already performs — carrying a dedicated `purpose=hangfire-dashboard`
claim and a 30-minute lifetime (distinct from the SPA's 15-minute access token, since the
dashboard polls its own live counters and a shorter window risked failing mid-review). It is set
as an httpOnly, `Secure`, `SameSite=Lax`, `Path=/hangfire` cookie. A `JwtBearerEvents.
OnMessageReceived` callback on the *existing* default scheme reads that cookie for requests under
`/hangfire` only; `OnTokenValidated` then rejects any token lacking the purpose claim — so even a
legitimately signed, currently-valid ordinary session token cannot authenticate the dashboard, and
this cookie can never authenticate anything outside `/hangfire`. No new authentication scheme was
registered; see research.md Decision 1 for why a second `AddCookie` scheme (and the Data
Protection key-ring risk that comes with it — this codebase has already been burned by an
ephemeral key ring breaking cookie-protected credentials on restart) was rejected in favor of
reusing the token infrastructure that already exists.

**Theming**: Two embedded-resource stylesheets (`hangfire-theme.css`, `hangfire-theme-dark.css`),
pinned to the same tokens `ClientApp/src/theme/tokens/palette.ts` exports, registered once at
startup via `Hangfire.Dashboard.DashboardRoutes.AddStylesheet`/`AddStylesheetDarkMode` with
`DashboardOptions.DarkModeEnabled = true`.

**The gap**: the spec's original intent — the dashboard opens already in whichever mode the admin
panel's own toggle is set to, decided at click time — turned out not to be implementable in
Hangfire.Core 1.8.24. Inspecting `Hangfire.Core.dll` directly (not just documentation) confirmed
its dashboard has **no persistence mechanism for dark mode at all**: no cookie, no
`localStorage`/`sessionStorage`, no query-string hook. `DarkModeStylesheetFiles` content is
wrapped by Hangfire itself in `@media (prefers-color-scheme: dark)`, and its own JS only calls
`window.matchMedia('(prefers-color-scheme: dark)')` — it never reads anything the app could set.
There is therefore no same-origin storage key to seed before opening the tab, and no
`?theme=`-style query parameter Hangfire would honor. Shipping such a parameter anyway — one that
silently does nothing — was rejected as its own kind of silent failure (§2 of the constitution).
The feature ships with **both stylesheets always registered**, so the dashboard's *palette colors*
always match Ask Lucy, but *which* stylesheet activates follows the browser/OS setting, not the
in-app toggle. `specs/060-hangfire-dashboard-access/spec.md` (FR-006, SC-003, User Story 2's
Acceptance Scenarios 2–3) and `quickstart.md` were both updated to record this as a documented,
accepted gap rather than assert a false equivalence.

## Alternatives considered

**A second `AddCookie` authentication scheme with `SignInAsync`.** Rejected — heavier (new scheme,
new ticket format) and depends on the Data Protection key ring, a failure class this app has
already hit once in production.

**Reverse-proxying `/hangfire` through an authenticated controller that injects the bearer
header server-side.** Rejected — Hangfire's dashboard ships its own routed sub-resources
(JS/CSS/AJAX polling under `/hangfire/...`); proxying all of them correctly is significantly more
code than a small `OnMessageReceived` callback for the same result.

**A one-time signed query-string token appended to the `/hangfire` URL.** Rejected — leaks into
browser history, referrer headers, and server access logs; a cookie does not.

**Forcing dark-mode selection via a custom injected JS toggle (`DashboardRoutes.AddJavaScript`).**
Considered as a way to close the theming gap rather than document it. `AddJavaScript` is real and
could inject a button that toggles a `body` class driven by a *custom* (non-`AddStylesheetDarkMode`)
stylesheet, seeded from Ask Lucy's own theme storage at page load. Deferred out of this feature's
scope — it is a genuinely new UI surface to build and test (a toggle button, its own CSS, reading
the SPA's theme state cross-tab), not a configuration flip, and User Story 2 was P2 (the dashboard
is fully usable in its default appearance for the P1 purpose). Left as a candidate for a follow-up
spec if the OS-driven behavior proves unacceptable in practice.

## Consequences

* Bearer-token infrastructure is reused end-to-end; no new package dependency, no data model
  change, no second authentication scheme to keep in sync with the first.
* The `purpose` claim and `Path=/hangfire` scoping mean this cookie cannot widen the attack
  surface of the rest of the app even if it were somehow read or replayed elsewhere.
* Any future change to `ITokenService`'s default lifetime does not silently affect the dashboard
  session, since it is minted with an explicit lifetime override rather than the shared default.
* The theming gap is a real, permanent product limitation under Hangfire 1.8.24 as shipped —
  not a bug to "eventually fix" without new work. Closing it requires building a bespoke toggle
  (see Alternatives above), which is out of scope here.
