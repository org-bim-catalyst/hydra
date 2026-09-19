# Tasks: Hangfire Dashboard Access from Admin Panel

**Input**: Design documents from `/specs/060-hangfire-dashboard-access/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/admin-hangfire-session.md, quickstart.md

**Tests**: Included — the constitution (§10, §19) requires tests for new behavior in the same
change that introduces it; they are not optional here.

**Organization**: Tasks are grouped by user story (US1 = P1 "open the dashboard from the
sidebar", US2 = P2 "dashboard matches app theme") so each can be delivered and validated on its
own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on an incomplete task)
- **[Story]**: US1 or US2, per spec.md priorities
- File paths are exact and relative to the repository root

---

## Phase 1: Setup

**Purpose**: Scaffolding only — no new package dependencies (Hangfire, JWT infra, MUI, TanStack
Query all already present per plan.md).

- [x] T001 [P] Create the empty directory `src/AskLucy.Application/Admin/Commands/IssueHangfireDashboardSession/` for the new command/handler
- [x] T002 [P] Create the directory `src/AskLucy.Web/HangfireTheme/` for the two theme stylesheets — corrected from `wwwroot/hangfire-theme/` during implementation, see research.md Decision 3's "Correction" note (Hangfire 1.8.x has no DashboardOptions.StylesheetFiles; stylesheets are embedded resources, not static wwwroot files)

**Checkpoint**: Directories exist; no behavior yet.

---

## Phase 2: Foundational

**Purpose**: Blocking prerequisites shared by more than one story.

None — US1 (auth) and US2 (theming) touch disjoint parts of the stack with no shared blocking
prerequisite beyond Phase 1. Proceed directly to Phase 3.

---

## Phase 3: User Story 1 - Open the background jobs dashboard from the admin panel (Priority: P1) 🎯 MVP

**Goal**: Administrator/Super User clicks a new sidebar entry and lands, already authenticated,
on the live Hangfire dashboard in a new tab; anyone else never sees the entry and is refused
server-side regardless.

**Independent Test**: As an Administrator, click the sidebar entry and confirm a new tab opens
showing live job data with no login/401/403 page; as a non-admin, confirm the entry is absent
and a direct `/hangfire` request is refused. (quickstart.md, "Validate User Story 1")

### Tests for User Story 1 ⚠️ write first, confirm they fail, then implement

- [x] T003 [P] [US1] Unit test: `TokenService.GenerateAccessToken` lifetime-override overload issues a token with the overridden expiry (default path unchanged) in `tests/AskLucy.Infrastructure.Tests/Auth/TokenServiceTests.cs`
- [x] T004 [P] [US1] Unit test: `IssueHangfireDashboardSessionCommandHandler` calls `ITokenService` with the caller's id, role claim, `purpose=hangfire-dashboard` claim, and the 30-minute override in `tests/AskLucy.Application.Tests/Admin/Commands/IssueHangfireDashboardSession/IssueHangfireDashboardSessionCommandHandlerTests.cs`
- [x] T005 [P] [US1] Integration test: `POST /api/v1/admin/hangfire/session` returns `204` and sets the `askLucyHangfireSession` cookie (`HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/hangfire`) for an Administrator, and `401`/`403` Problem Details for an unauthenticated/non-admin caller, in `tests/AskLucy.Web.Tests/Controllers/AdminHangfireSessionEndpointTests.cs`
- [x] T006 [P] [US1] Integration test: a request to `/hangfire` carrying a valid `askLucyHangfireSession` cookie authenticates via `HangfireDashboardAuthorizationFilter`, while a cookie whose token lacks the `purpose=hangfire-dashboard` claim (e.g., a normal SPA access token) is rejected, in `tests/AskLucy.Web.Tests/Auth/HangfireDashboardCookieAuthenticationTests.cs`
- [x] T007 [P] [US1] Frontend test: `useOpenHangfireDashboard` pre-opens a blank tab before awaiting the mint call, navigates it to `/hangfire` on success (no `?theme=` — see T025/research.md Decision 3, Hangfire has no query-param theme hook), shows a visible error and closes the blank tab on mint failure, and shows a visible "allow pop-ups" message when `window.open` returns `null` in `src/AskLucy.Web/ClientApp/src/features/admin/hooks/useOpenHangfireDashboard.test.ts`
- [x] T008 [P] [US1] Frontend test: the admin sidebar shows the "Jobs" entry only when `useIsAdmin()`/role state indicates Administrator or Super User, extending `src/AskLucy.Web/ClientApp/src/features/admin/components/AdminShell.test.tsx`

### Implementation for User Story 1

- [x] T009 [US1] Add a lifetime-override parameter to `ITokenService.GenerateAccessToken` in `src/AskLucy.Application/Abstractions/ITokenService.cs` (additive/backward-compatible — existing callers keep the configured default) — makes T003 pass
- [x] T010 [US1] Implement the overload in `src/AskLucy.Infrastructure/Auth/TokenService.cs` (depends on T009) — makes T003 pass
- [x] T011 [P] [US1] Create `HangfireDashboardCookie` (name, `Path=/hangfire`, `HttpOnly`, `Secure`, `SameSite=Lax`, 30-minute `MaxAge`, and the shared `purpose` claim type/value constants) in `src/AskLucy.Web/Auth/HangfireDashboardCookie.cs`, mirroring the structure of `src/AskLucy.Web/Auth/RefreshTokenCookie.cs`
- [x] T012 [US1] Create `IssueHangfireDashboardSessionCommand` and its handler in `src/AskLucy.Application/Admin/Commands/IssueHangfireDashboardSession/` — takes the caller's user id/role, calls `ITokenService.GenerateAccessToken` with the `purpose` claim and 30-minute override, logs a structured "Hangfire dashboard session issued" event, returns the token (depends on T010, T011) — makes T004 pass
- [x] T013 [US1] Add `POST /api/v1/admin/hangfire/session` to an admin controller in `src/AskLucy.Web/Controllers/v1/` — `[Authorize(Policy = "AdministratorOrSuperUser")]`, `[EnableRateLimiting("admin-endpoints")]`, dispatches T012's command via MediatR, writes the result into the response with `Response.Cookies.Append(HangfireDashboardCookie.Name, ...)`, returns `204 No Content` (depends on T011, T012) — makes T005 pass
- [x] T014 [US1] Extend the default JWT Bearer scheme's `Events.OnMessageReceived` in `src/AskLucy.Web/Program.cs` to read `HangfireDashboardCookie.Name` from `Request.Cookies` and set `ctx.Token` only for requests under `/hangfire`, and to reject (leave `ctx.Token` unset) if the decoded token's `purpose` claim doesn't match (depends on T011) — makes T006 pass
- [x] T015 [P] [US1] Add `postHangfireSession()` to `src/AskLucy.Web/ClientApp/src/features/admin/api/adminHangfireApi.ts` (POST to `/api/v1/admin/hangfire/session`, no response body expected)
- [x] T016 [US1] Implement `useOpenHangfireDashboard` in `src/AskLucy.Web/ClientApp/src/features/admin/hooks/useOpenHangfireDashboard.ts`: synchronously `window.open('', '_blank')` first, then await T015's call, then set the opened tab's `location.href` to `/hangfire`; on non-2xx response close the blank tab and surface a toast/inline error; if `window.open` returned `null`, surface a "allow pop-ups" message (depends on T015) — makes T007 pass
- [x] T017 [US1] Extend `AdminNavItem`/`ADMIN_NAV` rendering in `src/AskLucy.Web/ClientApp/src/features/admin/adminNav.tsx` and `src/AskLucy.Web/ClientApp/src/features/admin/components/AdminShell.tsx` to support an action-triggered entry (`onSelect` handler) alongside the existing path-based `RouterLink` entries, without changing behavior for existing entries
- [x] T018 [US1] Add the "Jobs" entry to `ADMIN_NAV` (`builtInOnly: true`, wired to T016's hook) in `src/AskLucy.Web/ClientApp/src/features/admin/adminNav.tsx` (depends on T016, T017) — makes T008 pass

**Checkpoint**: User Story 1 is fully functional and independently testable — an Administrator
can reach a working, authenticated dashboard from the sidebar; non-admins cannot, via either
path.

---

## Phase 4: User Story 2 - Dashboard looks and feels like part of Ask Lucy (Priority: P2)

**Goal**: The dashboard opens already matching the admin panel's current light/dark theme and
color palette, rather than Hangfire's stock appearance.

**Independent Test**: Toggle the admin panel's theme, open the dashboard in each mode, and
confirm each opens already matching that mode's colors with no manual re-toggle needed.
(quickstart.md, "Validate User Story 2")

### Tests for User Story 2 ⚠️ write first, confirm they fail, then implement

- [x] T019 [P] [US2] ~~Frontend test: `useOpenHangfireDashboard` appends `?theme=`~~ — superseded by T025's finding (no such param exists); no test added, see research.md Decision 3
- [x] T020 [P] [US2] Test that the embedded resources `Program.cs` registers via `DashboardRoutes.AddStylesheet`/`AddStylesheetDarkMode` exist and are non-empty, in `tests/AskLucy.Web.Tests/HangfireDashboardThemingTests.cs`

### Implementation for User Story 2

- [x] T021 [P] [US2] Author `src/AskLucy.Web/HangfireTheme/hangfire-theme.css` (light) overriding Hangfire's dashboard CSS classes with the values from `src/AskLucy.Web/ClientApp/src/theme/tokens/palette.ts` (primary `#1F4E5E`, secondary `#B8461F`, graphite neutral scale, radius tokens)
- [x] T022 [P] [US2] Author `src/AskLucy.Web/HangfireTheme/hangfire-theme-dark.css` (dark) using the same tokens' dark-mode values
- [x] T023 [US2] Register T021/T022's files via `DashboardRoutes.AddStylesheet`/`AddStylesheetDarkMode` and set `DashboardOptions.DarkModeEnabled = true` in `src/AskLucy.Web/Program.cs` (depends on T021, T022) — makes T020 pass; corrected from the originally-planned `DashboardOptions.StylesheetFiles` property, which doesn't exist in Hangfire.Core 1.8.24 (CS0117), see research.md Decision 3
- [x] T024 [US2] ~~Extend `useOpenHangfireDashboard` to append `?theme=`~~ — dropped, see T025
- [x] T025 [US2] **RESOLVED** — inspected `Hangfire.Core.dll` 1.8.24 directly (strings + embedded-resource CSS/JS): dark mode has no cookie, no `localStorage`/`sessionStorage` (absent from the bundle entirely), no query-param hook. `DarkModeStylesheetFiles` is wrapped by Hangfire itself in `@media (prefers-color-scheme: dark)`, and its JS only calls `matchMedia('(prefers-color-scheme: dark)')` to recolor charts. Fallback applied per this task's own documented contingency: always ship both stylesheets, accept the dashboard's OS-driven default. Documented in `research.md` Decision 3.

**Checkpoint**: Both user stories work independently and together — US1's dashboard access now
also opens visually matching the app's active theme.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T026 [P] Run `specs/060-hangfire-dashboard-access/quickstart.md` end to end (both roles, both themes, both failure paths) and record results — this is the only verification for SC-001 (click-to-dashboard under 5s) and SC-003 (100% theme match), so explicitly time step "Validate User Story 1" step 4 and note any mismatch found in step "Validate User Story 2"
- [x] T027 [P] Confirm `POST /api/v1/admin/hangfire/session` appears correctly in the generated OpenAPI document with accurate request/response schema and status codes (§6)
- [x] T028 [P] Accessibility test: keyboard operability and visible focus state for the new "Jobs" sidebar entry (§7, §19), in `src/AskLucy.Web/ClientApp/src/features/admin/components/AdminShell.a11y.test.tsx` — create this file following the existing per-page convention (see `AdminDashboardPage.a11y.test.tsx`) if `AdminShell` doesn't already have one
- [x] T029 Security review pass on this change (new auth-adjacent endpoint + cookie): confirm `Path=/hangfire` scoping, `SameSite=Lax`, `purpose` claim rejection path, and rate limiting all behave as designed under `tests/AskLucy.Web.Tests/` (§8, §19)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Empty — proceed straight from Setup to User Stories
- **User Story 1 (Phase 3)**: Depends on Setup only
- **User Story 2 (Phase 4)**: Depends on Setup only for its own tests/CSS/Program.cs wiring
  (T019, T024 extend the hook T016 already created in US1 — see note below)
- **Polish (Phase 5)**: Depends on both user stories being complete

### User Story Dependencies

- **US1 (P1)**: Independently deliverable as the MVP — a working, authenticated, unthemed
  dashboard link.
- **US2 (P2)**: Its CSS/`Program.cs` tasks (T021–T023, T025) are fully independent of US1. Its
  two hook-extension tasks (T019, T024) touch the same file US1 created (`useOpenHangfireDashboard.ts`)
  and should land after T016 exists, but add a query param only — they don't change US1's
  authenticated-access behavior, so US1 remains independently testable/deployable without US2.

### Parallel Opportunities

- T001, T002 in parallel (different directories)
- T003–T008 (all US1 tests) in parallel — different files
- T011 and T015 in parallel with each other and with T009 (different files, no shared dependency)
- T019, T020 (US2 tests) in parallel
- T021, T022 (the two CSS files) in parallel
- T026–T029 (Polish) in parallel

---

## Parallel Example: User Story 1

```bash
# Tests, launched together:
Task: "Unit test: TokenService lifetime-override overload in tests/AskLucy.Infrastructure.Tests/Auth/TokenServiceTests.cs"
Task: "Unit test: IssueHangfireDashboardSessionCommandHandler in tests/AskLucy.Application.Tests/Admin/Commands/IssueHangfireDashboardSession/IssueHangfireDashboardSessionCommandHandlerTests.cs"
Task: "Integration test: POST /api/v1/admin/hangfire/session in tests/AskLucy.Web.Tests/Controllers/AdminHangfireSessionEndpointTests.cs"
Task: "Integration test: /hangfire cookie authentication in tests/AskLucy.Web.Tests/Auth/HangfireDashboardCookieAuthenticationTests.cs"
Task: "Frontend test: useOpenHangfireDashboard in .../useOpenHangfireDashboard.test.ts"
Task: "Frontend test: sidebar entry visibility in .../AdminShell.test.tsx"

# Independent implementation files, launched together once their own deps are met:
Task: "HangfireDashboardCookie in src/AskLucy.Web/Auth/HangfireDashboardCookie.cs"
Task: "postHangfireSession() in src/AskLucy.Web/ClientApp/src/features/admin/api/adminHangfireApi.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 3: User Story 1
3. **STOP and VALIDATE**: run quickstart.md's User Story 1 section
4. Deploy/demo — administrators can already reach a working (unthemed) dashboard

### Incremental Delivery

1. Setup → Foundation ready (trivial here)
2. US1 → validate independently → deploy/demo (MVP)
3. US2 → validate independently → deploy/demo (visual polish on top of US1)
4. Polish

---

## Notes

- [P] tasks touch different files with no unfinished dependency between them
- [Story] label maps every Phase 3/4 task to spec.md's US1/US2
- Tests are written first per task group and must fail before their corresponding
  implementation task is started (constitution §10)
- Commit after each task or logical group
- T025 is deliberately investigative — its outcome may change the exact mechanism (localStorage
  key vs. accepted fallback) without changing anything about US1 or the rest of US2
