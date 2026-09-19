# Research: Hangfire Dashboard Access from Admin Panel

## Decision 1: How the dashboard authenticates a browser tab

**Decision**: Extend the *existing* default JWT Bearer scheme to also accept a token from an
httpOnly cookie, but only for requests under `/hangfire`, via `JwtBearerOptions.Events.
OnMessageReceived`. Mint that token by reusing the existing `ITokenService.GenerateAccessToken`
(same signing key, same validation the bearer scheme already performs), not a hand-rolled token
format. No new ASP.NET Core authentication scheme is registered.

**Rationale**:
- This codebase already hit a real production incident from a *different* cookie-authentication
  mechanism: a bare `AddDataProtection()` with no persisted key ring meant every cookie-protected
  credential broke on each app restart (see prior incident notes for the Data Protection key
  ring). A full `AddCookie(...)` scheme with `SignInAsync` encrypts its ticket through that same
  Data Protection stack. Reusing the JWT signing key — which is already a configured, persisted
  secret, not an ephemeral machine key — sidesteps that entire failure class.
- `ITokenService` already exists, is already Infrastructure-implemented and Application-visible,
  and already produces exactly the token shape (`AccessTokenResult`) the default scheme already
  knows how to validate. Reusing it is the smallest change that satisfies Simplicity First
  (Principle III) — it's config (where does `ctx.Token` come from), not new infrastructure.
- `HangfireDashboardAuthorizationFilter` needs **no changes**: once `OnMessageReceived` populates
  `ctx.Token` from the cookie, the default scheme validates it exactly as it would an
  `Authorization` header, and `HttpContext.User` is populated the same way for both paths.

**Alternatives considered**:
- *Register a second `AddCookie` scheme, `SignInAsync` on mint, `AuthenticateAsync` it explicitly
  in the dashboard filter.* Rejected — heavier (new scheme, new ticket format), and depends on
  the Data Protection key ring this app has already been burned by once.
- *Reverse-proxy `/hangfire` through an authenticated controller that injects the bearer header
  server-side.* Rejected — Hangfire's dashboard ships its own routed sub-resources (JS/CSS/AJAX
  polling endpoints under `/hangfire/...`); proxying all of them correctly is significantly more
  code than a 10-line `OnMessageReceived` callback for comparable benefit.
- *A one-time signed query-string token appended to the `/hangfire` URL.* Rejected — leaks into
  browser history, referrer headers, and server access logs; a cookie does not.

## Decision 2: CSRF posture of the new cookie

**Decision**: `SameSite=Lax`, `Path=/hangfire`, `HttpOnly`, `Secure`, short lifetime (see
Decision 4), plus a dedicated claim (e.g. `purpose=hangfire-dashboard`) that `OnMessageReceived`
requires before treating the cookie's token as valid for `/hangfire` — even a legitimately
signed access token minted for a different purpose is rejected on that path.

**Rationale**: §8 of the constitution requires either anti-forgery tokens or "bearer-token-only
auth with no ambient cookie auth" for CSRF protection; this feature necessarily introduces one
ambient cookie, so it must be defended directly. `SameSite=Lax` still permits the one legitimate
cross-site case (the Vite dev server, a different origin/scheme than the API in local
development, navigating to `/hangfire` via `window.open` — a top-level GET) while blocking the
case that actually matters: a forged cross-site POST hitting Hangfire's own job-mutating AJAX
endpoints, which Lax never attaches ambient cookies to. `Path=/hangfire` means the cookie is
never sent to any bearer-authenticated JSON API call, so it can't accidentally widen the attack
surface of the rest of the app. The `purpose` claim means even if this cookie were somehow read
or replayed, it authenticates nothing outside `/hangfire`.

**Alternatives considered**:
- *`SameSite=Strict`*, matching the tightest option. Rejected — breaks the dev-environment
  cross-origin top-level navigation from the Vite dev server to the API, which `SameSite=None`
  is already needed for elsewhere in this app for a different (fetch-based) reason; `Lax` is the
  correct middle ground for a top-level-navigation-only use case.
- *`SameSite=None`, matching `RefreshTokenCookie`.* Rejected — `RefreshTokenCookie` needs `None`
  because it's sent via credentialed `fetch()`, a genuinely cross-site XHR case in dev. This
  cookie is only ever used via top-level navigation, so it doesn't need `None`'s broader (and
  CSRF-riskier) reach, and Hangfire's own dashboard actions are exactly the kind of mutating POST
  `None` would leave exposed to forgery.

## Decision 3: Theming a third-party server-rendered dashboard

**Decision**: Ship two small static CSS files (`hangfire-theme.css`, `hangfire-theme-dark.css`)
under `AskLucy.Web/HangfireTheme/`, embedded into the assembly (`<EmbeddedResource>` in
`AskLucy.Web.csproj`) and registered once at startup via `DashboardRoutes.AddStylesheet`/
`AddStylesheetDarkMode(typeof(Program).Assembly, "AskLucy.Web.HangfireTheme.<file>.css")`, with
`DashboardOptions.DarkModeEnabled = true`. Color values are pinned to the same values already
exported from `ClientApp/src/theme/tokens/palette.ts` (primary `#1F4E5E`, secondary `#B8461F`,
the graphite neutral scale, and the existing radius scale).

**Correction during implementation**: `DashboardOptions.StylesheetFiles`/`DarkModeStylesheetFiles`
— the properties this decision originally named, and what tasks.md T021–T023 were written
against — **do not exist** in Hangfire.Core 1.8.24 (confirmed by `dotnet build` failing with
CS0117, then by inspecting `Hangfire.Core.xml`'s actual public API). The real 1.8.x mechanism is
`Hangfire.Dashboard.DashboardRoutes.AddStylesheet(Assembly, string)`/
`AddStylesheetDarkMode(Assembly, string)` — static, global, embedded-resource-based registration
— plus the single `DashboardOptions.DarkModeEnabled` bool. The files therefore live outside
`wwwroot` (they are never served as static, URL-addressable content) and are compiled into the
assembly instead. This doesn't change the visual outcome, only how the two files reach Hangfire.

**T025 finding (confirmed by inspecting `Hangfire.Core.dll` 1.8.24 directly, not just docs)**:
Hangfire's dashboard dark mode has **no persistence mechanism at all** — no cookie, no
`localStorage`/`sessionStorage` (verified absent from the embedded JS bundle), no query string
hook. `DashboardOptions.DarkModeStylesheetFiles` content is wrapped by Hangfire itself in
`@media (prefers-color-scheme: dark) { ... }` (confirmed identical to how its own built-in
`dark.css` ships), and the dashboard's JS only uses `window.matchMedia('(prefers-color-scheme:
dark)')` to recolor its live charts — it never reads a cookie, storage key, or URL param to pick
a stylesheet. There is therefore no same-origin storage key to seed and no query-param-to-cookie
bridge to build; T025's own documented fallback applies: **always ship both stylesheets and
accept the dashboard's OS-driven default**. `useOpenHangfireDashboard` does not append a
`?theme=` param — Hangfire would ignore it, and shipping a parameter with no effect would be
worse than not shipping one (§2 No Silent Failures reasoning: a dead code path silently doing
nothing is itself a kind of silent failure). T019/T024 in tasks.md are adjusted accordingly:
instead of asserting a `?theme=` param, the US2 test asserts `hangfire-theme.css`/
`hangfire-theme-dark.css` exist, are non-empty, and are wired into `DashboardOptions`; SC-003
("100% theme match") is verified manually in quickstart.md against the tester's own OS color
scheme setting, not against the SPA's in-app theme toggle, and quickstart.md documents that gap
explicitly rather than asserting a false equivalence.

**Rationale**: Hangfire's dashboard is not a React/MUI surface — it renders its own HTML/CSS
server-side and ships its own light/dark toggle. There is no route to make it "just use the MUI
theme provider" (§7's literal mechanism) since it isn't MUI at all. The closest compliant
reading of §7 available for a genuinely third-party rendered surface is: don't invent new,
unrelated colors — derive the override from the *same* token source everything else in the app
already uses, so a future palette change (editing `palette.ts`) is the one place that needs to
change, not two.

**Alternatives considered**:
- *Leave Hangfire's default look untouched.* Rejected per explicit requirement (User Story 2 /
  FR-006, FR-007) — not a hard blocker for MVP but the user asked for it explicitly for this
  feature, not as a "future nice-to-have."
- *Embed the dashboard in an iframe styled from the parent.* Rejected in an earlier round of
  this investigation (adds CSP/`X-Frame-Options` complexity and iframe popup-blocker-adjacent
  fragility) in favor of a plain new tab — theming has to happen server-side either way since
  cross-origin iframe content can't be restyled from the parent frame regardless.

## Decision 4: Dashboard session lifetime

**Decision**: 30 minutes, distinct from the SPA's 15-minute access-token lifetime — requires a
small additive change to token issuance (an explicit lifetime override rather than always
reading `JwtOptions.AccessTokenLifetimeMinutes`), since Hangfire's dashboard polls its own live
counters every few seconds and a 15-minute window risks failing mid-review (an edge case the
spec calls out explicitly).

**Rationale**: SC-004 sets this expectation directly; reusing the 15-minute default would
under-deliver against an explicit, already-agreed success criterion for a workflow (reviewing
job history) that plausibly runs longer than a typical chat interaction.

**Alternatives considered**:
- *Reuse the existing 15-minute access-token lifetime unchanged (avoid any `ITokenService`
  change).* Considered for simplicity, but rejected — it directly contradicts SC-004, and the
  edge case in the spec ("further activity in that tab must fail safely... rather than exposing
  job data past the intended session") implies the window should comfortably outlast a normal
  review, not match the shortest-lived token in the system.

## Summary

No `[NEEDS CLARIFICATION]` markers were carried over from the spec. All four decisions above
are additive to existing infrastructure (`ITokenService`, the default JWT Bearer scheme, the
`admin-endpoints` rate-limit policy, `theme/tokens/palette.ts`) — no new authentication scheme,
no new package dependency, no data model change.
