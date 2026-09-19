# Implementation Plan: Hangfire Dashboard Access from Admin Panel

**Branch**: `060-hangfire-dashboard-access` | **Date**: 2026-09-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/060-hangfire-dashboard-access/spec.md`

## Summary

The Hangfire dashboard already runs at `/hangfire`, gated to Administrator/Super User by
`HangfireDashboardAuthorizationFilter`, but it is unreachable today because that filter only
ever sees an authenticated `HttpContext.User` when a request carries a JWT `Authorization`
header — which a plain browser navigation (new tab) never does. This feature adds: (1) a
bearer-authenticated endpoint that mints a short-lived, purpose-scoped access token into an
httpOnly cookie the browser will present on the *next* navigation to `/hangfire`, (2) a small
extension to the existing JWT Bearer handler so it also accepts that cookie specifically on
`/hangfire` requests (no new ASP.NET Core authentication scheme, no Data Protection dependency),
and (3) a sidebar entry + click handler in the admin panel that calls the mint endpoint, then
opens `/hangfire` in a new tab with a `?theme=` hint. `DashboardOptions.StylesheetFiles`/
`DarkModeStylesheetFiles` apply CSS overrides so the dashboard's colors match the app's palette
tokens in both light and dark mode.

## Technical Context

**Language/Version**: C# / .NET 10 (backend), TypeScript / React 19 (frontend)

**Primary Dependencies**: ASP.NET Core, MediatR, Hangfire.AspNetCore 1.8.24 (already installed),
MUI, TanStack Query, Zustand — no new package dependencies required.

**Storage**: N/A — the dashboard-access token is stateless (JWT-style, signature-validated,
short expiry), nothing new is persisted.

**Testing**: xUnit + NSubstitute (backend), Vitest + Testing Library (frontend) — matching
existing suites under `tests/` and `ClientApp/src/**/*.test.tsx`.

**Target Platform**: Existing ASP.NET Core host (`AskLucy.Web`) serving both the API and the
built SPA; no new deployment target.

**Project Type**: Web application (existing backend + `ClientApp` frontend, both already present).

**Performance Goals**: Sidebar click to a working dashboard tab in under 5 seconds (SC-001) —
dominated by one small mint request, not a performance-sensitive path.

**Constraints**: Must not introduce a new ASP.NET Core authentication scheme or rely on the
Data Protection key ring — see Research Decision 1 (this codebase has a documented history of
an ephemeral/non-persisted Data Protection key ring silently invalidating cookie-protected
credentials on restart; a design that avoids that dependency entirely is preferred over one
that reintroduces it). Must satisfy the constitution's CSRF requirement (§8) despite adding a
new ambient (ambient-on-navigation) cookie.

**Scale/Scope**: Single new endpoint, one cookie, one filter change, one sidebar entry, one
CSS file. No data model changes, no migrations.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **§3 Architecture Rules / Clean Architecture (Principle I)**: The token-minting logic goes
  through a MediatR command in `Application`, calling the existing `ITokenService` abstraction
  (already Infrastructure-implemented, already Application-visible) — no new Application →
  Infrastructure/ASP.NET Core reference. Cookie-writing (an `HttpResponse` concern) stays in
  the `AskLucy.Web` (Api) layer, mirroring how `AuthController` already writes
  `RefreshTokenCookie`. **PASS.**
- **§4/§6 API Standards**: New endpoint is versioned (`/api/v1/admin/hangfire/session`),
  `[Authorize(Policy = "AdministratorOrSuperUser")]` (existing policy, reused verbatim), rate
  limited under the existing `admin-endpoints` policy, documented via OpenAPI, and returns
  Problem Details on failure. **PASS.**
- **§7 UI Principles / Theming**: The dashboard is a third-party-rendered page, not MUI — the
  "both themes supported, no hardcoded colors bypassing the theme" rule is satisfied by
  deriving the CSS override file's color values from the same palette tokens
  (`theme/tokens/palette.ts`) at build/config time rather than hand-picking new colors, and by
  selecting light/dark per request from the SPA's current theme instead of a fixed default.
  **PASS** (see Research Decision 3 for how "no hardcoded colors" is reconciled with Hangfire
  needing a static server-side stylesheet).
- **§8 Security — CSRF**: Adding a cookie that a browser attaches automatically is exactly the
  ambient-cookie pattern §8 is wary of. Addressed by: `SameSite=Lax` (blocks the cookie on any
  cross-site forged request — including the POSTs Hangfire's own dashboard uses for
  job actions — while still allowing the top-level `window.open` navigation that legitimately
  needs it), `Path=/hangfire` (never attached to any other endpoint), `HttpOnly` + `Secure`,
  short lifetime, and a dedicated claim scoping the token to dashboard use only so it cannot be
  replayed as a general bearer token even if somehow exfiltrated. See Research Decision 2.
  **PASS.**
- **§8 Security — Least privilege / audit**: Token minting is itself an authentication-adjacent
  event and is written to the existing audit trail, matching the "authentication events...MUST
  be written to an immutable audit trail" rule. **PASS.**
- **§2.VIII No Silent Failures**: Mint failures and pop-up-blocked failures both have an explicit,
  visible UI error path (FR-008, FR-009) — no bare `console.error`, no un-awaited click handler.
  **PASS.**

No violations requiring the Complexity Tracking table.

## Project Structure

### Documentation (this feature)

```text
specs/060-hangfire-dashboard-access/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
src/AskLucy.Application/
└── Admin/
    └── Commands/
        └── IssueHangfireDashboardSession/
            ├── IssueHangfireDashboardSessionCommand.cs
            └── IssueHangfireDashboardSessionCommandHandler.cs

src/AskLucy.Web/
├── Auth/
│   └── HangfireDashboardCookie.cs         # name/path/options, mirrors RefreshTokenCookie.cs
├── Auth/
│   └── HangfireDashboardAuthorizationFilter.cs   # existing file — role check unchanged
├── Controllers/v1/
│   └── AdminController.cs (or a new AdminHangfireController.cs)  # POST session endpoint
├── wwwroot/
│   └── hangfire-theme/
│       ├── hangfire-theme.css             # light palette override (StylesheetFiles)
│       └── hangfire-theme-dark.css        # dark palette override (DarkModeStylesheetFiles)
└── Program.cs                              # JwtBearerEvents.OnMessageReceived extension;
                                             # UseHangfireDashboard StylesheetFiles wiring

src/AskLucy.Web/ClientApp/src/features/admin/
├── adminNav.tsx                            # new "Jobs" entry
├── api/adminHangfireApi.ts                 # calls the mint endpoint
└── hooks/useOpenHangfireDashboard.ts        # click handler: window.open + mint + navigate

tests/AskLucy.Application.Tests/Admin/Commands/IssueHangfireDashboardSession/
tests/AskLucy.Web.Tests/Controllers/... (or equivalent integration test location)
src/AskLucy.Web/ClientApp/src/features/admin/hooks/useOpenHangfireDashboard.test.ts
```

**Structure Decision**: Existing Clean Architecture layering (`Domain` unused — no new
entities — `Application` → `Infrastructure` → `AskLucy.Web` Api) plus the existing
`ClientApp` React frontend under `AskLucy.Web`. No new projects.

## Complexity Tracking

*No constitution violations — table not needed.*
