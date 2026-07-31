---

description: "Task list for Cookie Consent & Privacy Management"
---

# Tasks: Cookie Consent & Privacy Management

**Input**: Design documents from `/specs/004-cookie-consent-privacy/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/cookie-consent-api.md](./contracts/cookie-consent-api.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution (§10 Testing Standards, non-negotiable — "Tests are
written for new behavior in the same PR that introduces it") requires unit, integration,
and Playwright E2E coverage for new/changed behavior — test tasks are not optional here.

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P2/P3) so each
story is independently implementable, testable, and demoable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Maps the task to US1–US3 from spec.md
- All descriptions include exact file paths

## Path Conventions

Existing single-solution web app (constitution §3): `src/AskLucy.Domain`,
`src/AskLucy.Application`, `src/AskLucy.Infrastructure`, `src/AskLucy.Persistence`,
`src/AskLucy.Web` (API + `ClientApp/` React SPA), `tests/AskLucy.*.Tests`. This feature
adds a new `Consent` feature folder to each backend project and two new frontend feature
folders (`features/consent`, `features/privacy`) — no new top-level project (plan.md
Project Structure).

---

## Phase 1: Setup

**Purpose**: The one piece of configuration scaffolding every later task depends on.

- [X] T001 [P] Add a `CookiePolicy` section (`CurrentVersion`, `EffectiveAtUtc`) to
  `src/AskLucy.Web/appsettings.json` and `src/AskLucy.Web/appsettings.Development.json`
  with an initial version string and today's date (research.md Topic 3)

**Checkpoint**: No package installs needed — this feature reuses the existing stack
(plan.md Technical Context: no new backend or frontend dependency).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The append-only `CookieConsentRecord` entity, its persistence, the
policy-version abstraction, and DI/rate-limit wiring every user story depends on.

**⚠️ CRITICAL**: No user story task may begin until this phase is complete and the
solution builds with the new migration applied.

- [X] T002 [P] Create `CookieConsentRecord` Domain entity — `BaseEntity`, `UserId`,
  `PolicyVersion`, `FunctionalAccepted`, `AnalyticsAccepted`, `MarketingAccepted`; a
  `Create(...)` factory validating non-blank `UserId`/`PolicyVersion`; **no mutator
  methods** (append-only, data-model.md) in `src/AskLucy.Domain/Consent/CookieConsentRecord.cs`
- [X] T003 [P] Create `IUserCookieConsentRepository` interface — `GetLatestAsync(string
  userId, ct)`, `AddAsync(CookieConsentRecord record, ct)`, and `GetHistoryAsync(string
  userId, ct)` (all rows for a user ordered by `CreatedAtUtc DESC` — satisfies FR-016's
  "what was this user's consent state on date X," data-model.md) in
  `src/AskLucy.Application/Abstractions/IUserCookieConsentRepository.cs`
- [X] T004 [P] Create `ICookiePolicyProvider` interface — `GetCurrentPolicy()` returning
  `(string Version, DateTime EffectiveAtUtc)` in
  `src/AskLucy.Application/Abstractions/ICookiePolicyProvider.cs`
- [X] T005 [P] Create `CookiePolicyOptions` class — `CurrentVersion` (`[Required]`,
  non-empty string) and `EffectiveAtUtc` (`DateTime`), so `ValidateOnStart` (T011,
  constitution §4) actually rejects a missing/blank version at startup instead of
  validating nothing in `src/AskLucy.Infrastructure/Consent/CookiePolicyOptions.cs`
- [X] T006 Create `CookiePolicyProvider` implementing `ICookiePolicyProvider`, reading
  `IOptions<CookiePolicyOptions>` (constitution §4) in
  `src/AskLucy.Infrastructure/Consent/CookiePolicyProvider.cs` (depends on T004, T005)
- [X] T007a [P] Add a `CookieConsentRecords` navigation property (`ICollection<CookieConsentRecord>`)
  to `ApplicationUser`, mirroring its existing `UserChats` navigation, so the FK
  relationship in T007 can target it (spec.md Edge Cases — account deletion) in
  `src/AskLucy.Persistence/Identity/ApplicationUser.cs` (depends on T002)
- [X] T007 Create `CookieConsentRecordConfiguration` — `IEntityTypeConfiguration<CookieConsentRecord>`
  with a composite index on `(UserId, CreatedAtUtc DESC)` (constitution §5, data-model.md
  Index) **and** a `HasOne<ApplicationUser>().WithMany(u => u.CookieConsentRecords)
  .OnDelete(DeleteBehavior.Cascade)` foreign key, mirroring `UserChatConfiguration`
  exactly — this is what makes account deletion also remove a user's consent history
  (spec.md Edge Cases: "retained or deleted according to the same data-retention rules
  applied to the rest of their account data") in
  `src/AskLucy.Persistence/Configurations/CookieConsentRecordConfiguration.cs`
  (depends on T002, T007a)
- [X] T008 Add `DbSet<CookieConsentRecord> CookieConsentRecords` to `AskLucyDbContext` in
  `src/AskLucy.Persistence/AskLucyDbContext.cs` (depends on T007)
- [X] T009 Create `UserCookieConsentRepository` implementing `IUserCookieConsentRepository`
  (`GetLatestAsync` orders by `CreatedAtUtc DESC`; `GetHistoryAsync` returns all rows for a
  user ordered by `CreatedAtUtc DESC`; `AddAsync` inserts — never updates an existing row)
  in `src/AskLucy.Persistence/Repositories/UserCookieConsentRepository.cs` (depends on
  T003, T008)
- [X] T010 Generate the EF Core migration adding the `CookieConsentRecords` table, its
  index, and its cascade-delete FK to `ApplicationUser` via `dotnet ef migrations add
  AddCookieConsent -p src/AskLucy.Persistence -s src/AskLucy.Web`; verify `Down()` is
  reversible (constitution §5 Migrations) (depends on T007, T008)
- [X] T011 Bind `CookiePolicyOptions` with
  `.ValidateDataAnnotations().ValidateOnStart()` (constitution §4 — enforces T005's
  `[Required]` on `CurrentVersion`), register `ICookiePolicyProvider` and
  `IUserCookieConsentRepository` in DI, and add a new `consent-endpoints` rate-limit
  policy following the exact `admin-endpoints`/`chat-endpoints` shape (fixed window,
  partitioned by `User.Identity.Name ?? RemoteIpAddress ?? "anonymous"`, research.md
  Topic 10) (depends on T001, T006, T009) — implemented as: options binding +
  `ICookiePolicyProvider` registration in `AddInfrastructure` (`src/AskLucy.Infrastructure/DependencyInjection.cs`),
  `IUserCookieConsentRepository` registration in `AddPersistence`
  (`src/AskLucy.Persistence/DependencyInjection.cs`), and only the rate-limit policy in
  `src/AskLucy.Web/Program.cs` — matching this codebase's actual established convention
  (JwtOptions/IUserProfileRepository etc. are bound/registered in the extension methods,
  not inline in Program.cs), not the task's literal "in Program.cs" wording

**Checkpoint**: Solution builds; `dotnet ef database update` succeeds; no user-facing
behavior has changed yet. User story work can now begin.

---

## Phase 3: User Story 1 - First-Login Cookie Consent Banner (Priority: P1) 🎯 MVP

**Goal**: A user with no recorded consent decision sees a blocking banner on the main app
page (Accept All / Reject Non-Essential / Customize per category, Essential always on);
their choice is saved to their account and the banner never reappears under the same
policy version.

**Independent Test**: Log in with a fresh account, confirm the banner blocks all other
interaction until a choice is made, confirm each of the three choices persists correctly,
and confirm the banner does not reappear on the next login (quickstart.md Scenario 1 & 2).

### Tests for User Story 1

- [X] T012 [P] [US1] Unit tests for `CookieConsentRecord.Create` (rejects blank
  `UserId`/`PolicyVersion`; confirm no mutator method exists/entity is immutable) in
  `tests/AskLucy.Domain.Tests/Consent/CookieConsentRecordTests.cs`
- [X] T013 [P] [US1] Unit tests for `GetMyCookieConsentQueryHandler` — no record →
  `hasConsented: false`/`requiresReconsent: true`; latest record matches current policy
  version → `requiresReconsent: false`; latest record's version differs from current →
  `requiresReconsent: true` (data-model.md `CookieConsentStatusDto`) in
  `tests/AskLucy.Application.Tests/Consent/GetMyCookieConsentQueryHandlerTests.cs`
- [X] T014 [P] [US1] Unit tests for `SaveMyCookieConsentCommandHandler` — always inserts a
  **new** `CookieConsentRecord` stamped with the current policy version (never mutates an
  existing row); validator rejects a request with a missing boolean field in
  `tests/AskLucy.Application.Tests/Consent/SaveMyCookieConsentCommandHandlerTests.cs` —
  discovered mid-implementation that `bool` (non-nullable) can't distinguish "omitted" from
  "false" over JSON, so `SaveMyCookieConsentCommand`'s three fields are `bool?`, not `bool`
  (also corrected in contracts/cookie-consent-api.md and `SaveCookieConsentRequest`)
- [X] T015 [P] [US1] Integration tests: `UserCookieConsentRepository.GetLatestAsync` returns
  the most recently created row for a user across multiple inserts; `GetHistoryAsync`
  returns every row for a user ordered by `CreatedAtUtc DESC` (FR-016); deleting the
  owning `ApplicationUser` cascades and removes all of that user's `CookieConsentRecords`
  (spec.md Edge Cases, T007's FK) in
  `tests/AskLucy.Persistence.Tests/Consent/UserCookieConsentRepositoryTests.cs` — written
  and builds cleanly; not executed in this sandbox (no Docker available for the
  Testcontainers-backed `PersistenceTestFixture`), same limitation noted in
  specs/002-chat-history-management's tasks.md T078
- [X] T016 [P] [US1] Unit test: `CookiePolicyProvider` returns the values bound from
  `IOptions<CookiePolicyOptions>` in
  `tests/AskLucy.Infrastructure.Tests/Consent/CookiePolicyProviderTests.cs`
- [X] T017 [P] [US1] Controller tests for `CookieConsentController`'s `me` endpoints — `401`
  with no bearer token; `200` with the expected `CookieConsentResponse` shape for an
  authenticated user; `PUT` returns `400` Problem Details when a required boolean field is
  omitted in `tests/AskLucy.Web.Tests/Consent/CookieConsentControllerTests.cs` — no live DB
  in `CustomWebApplicationFactory` (existing convention), so the "200 with expected shape"
  assertion is the same "authorization passes through, never 401" idiom used by
  `LockUnlockUserTests`, not a full body-shape assertion. **Found and fixed a real,
  pre-existing bug** while writing the 400 assertion: `ProblemDetailsMiddleware` set
  `Response.ContentType = "application/problem+json"` but then called
  `WriteAsJsonAsync(problemDetails)` with no explicit content type, which unconditionally
  overwrites it back to `application/json` — silently breaking constitution §6's RFC 9457
  requirement for every error response app-wide, not just this feature's. Fixed by passing
  the content type explicitly to `WriteAsJsonAsync`; verified via a full `AskLucy.Web.Tests`
  run (72/72 passing, no regressions). Also added `CookiePolicy:CurrentVersion`/
  `EffectiveAtUtc` to `CustomWebApplicationFactory`'s in-memory config — otherwise this
  feature's new `ValidateOnStart()` would have thrown at host startup for every test using
  that factory, not just this feature's
- [X] T018 [US1] Playwright E2E: fresh login shows the blocking banner; Accept All, Reject
  Non-Essential, and Customize each persist correctly and block interaction until chosen;
  banner does not reappear on next login (quickstart.md Scenario 1 & 2) in
  `tests/AskLucy.E2E.Tests/CookieConsentBanner.spec.ts` — not runnable in this sandbox (no
  running backend/frontend deployment), same documented limitation as every other spec in
  this directory; verified via `playwright test --list` that all 4 scenarios parse
  correctly

### Implementation for User Story 1

- [X] T019 [US1] Create `CookieConsentStatusDto` (`HasConsented`, `RequiresReconsent`,
  `PolicyVersion`, `CurrentPolicyVersion`, `Essential` (constant `true`), `Functional`,
  `Analytics`, `Marketing`, `LastUpdatedAtUtc`) in
  `src/AskLucy.Application/Consent/CookieConsentStatusDto.cs`
- [X] T020 [US1] Create `GetMyCookieConsentQuery`/`GetMyCookieConsentQueryHandler` —
  resolves `ICurrentUserAccessor.UserId`, calls `IUserCookieConsentRepository.GetLatestAsync`
  and `ICookiePolicyProvider.GetCurrentPolicy`, computes `RequiresReconsent` in
  `src/AskLucy.Application/Consent/Queries/GetMyCookieConsent/GetMyCookieConsentQuery.cs`
  (+ Handler) (depends on T003, T004, T019, T013 failing first)
- [X] T021 [US1] Create `SaveMyCookieConsentCommand`/`SaveMyCookieConsentCommandHandler`/
  `SaveMyCookieConsentCommandValidator` — resolves the acting user, inserts a new
  `CookieConsentRecord.Create(...)` stamped with the current policy version, returns the
  updated `CookieConsentStatusDto` in
  `src/AskLucy.Application/Consent/Commands/SaveMyCookieConsent/SaveMyCookieConsentCommand.cs`
  (+ Handler + Validator) (depends on T002, T003, T004, T019, T014 failing first) — also
  includes T043's structured Serilog logging (folded in here rather than deferred to
  Polish, since it's natural to add while the handler is already being written)
- [X] T022 [US1] Create `Contracts/CookieConsentContracts.cs` —
  `SaveCookieConsentRequest(bool? Functional, bool? Analytics, bool? Marketing)` (nullable,
  see T014's note), `CookieConsentResponse(...)` (contracts/cookie-consent-api.md) in
  `src/AskLucy.Web/Contracts/CookieConsentContracts.cs` (depends on T019) — also includes
  `CookiePolicyResponse` (US3/T037) in the same file, since it's the same small contracts
  file and avoids a second near-empty file later
- [X] T023 [US1] Create `CookieConsentController` — `[Authorize]` class-level,
  `[EnableRateLimiting("consent-endpoints")]`, `GET`/`PUT api/v1/users/me/cookie-consent`
  actions in `src/AskLucy.Web/Controllers/v1/CookieConsentController.cs` (depends on T020,
  T021, T022, T017 failing first)
- [X] T024 [P] [US1] Create `consentApi.ts` — `getMyCookieConsent()`, `saveMyCookieConsent(...)`
  thin `apiFetch` wrappers in
  `src/AskLucy.Web/ClientApp/src/features/consent/api/consentApi.ts`
- [X] T025 [US1] Create `useCookieConsent.ts` — a TanStack Query `useQuery` for status plus
  a `useMutation` for saving that updates/invalidates the status query's cache on success
  (so the change is reflected immediately, no reload — FR-014/SC-004) with an explicit
  `onError` path surfaced to the caller (constitution §2.VIII) in
  `src/AskLucy.Web/ClientApp/src/features/consent/hooks/useCookieConsent.ts` (depends on
  T024)
- [X] T025a [P] [US1] Create `cookieCategories.ts` — the single shared constant listing
  each category's key, display name, and description (Essential, Functional, Analytics,
  Marketing), imported by the banner, the Settings panel, and the Privacy Page so category
  text is centralized in one place, never hardcoded per component (FR-021, research.md
  Topic 4) in `src/AskLucy.Web/ClientApp/src/features/consent/cookieCategories.ts`
- [X] T026 [US1] Create `CookieConsentBanner.tsx` — MUI `Dialog` with `open` hardcoded and a
  no-op `onClose` (non-dismissible, FR-020 — this MUI version removed
  `disableEscapeKeyDown`; leaving `open` uncontrolled by `onClose` achieves the same
  result, research.md Topic 6); Accept All / Reject Non-Essential / Customize (per-category
  toggles sourced from `cookieCategories.ts`, Essential rendered locked-on); includes a
  link to `/privacy` (FR-008 — functional once T042 adds the route) in
  `src/AskLucy.Web/ClientApp/src/features/consent/components/CookieConsentBanner.tsx`
  (depends on T025, T025a)
- [X] T027 [US1] Create `ConsentGate.tsx` — while `useCookieConsent` is loading, renders a
  full-page blocking loading state (no gap where the app is interactive without a
  decision); once resolved, renders `CookieConsentBanner` over the children when
  `requiresReconsent` is `true`, otherwise renders children normally in
  `src/AskLucy.Web/ClientApp/src/features/consent/components/ConsentGate.tsx` (depends on
  T025, T026) — also handles the query's own `isError` state with a visible retry Alert
  (constitution §2.VIII), which the task description didn't call out explicitly but FR-017
  requires for any consent load/save failure, not saves alone
- [X] T028 [US1] Wire `ConsentGate` around `ProtectedRoute`'s children in
  `src/AskLucy.Web/ClientApp/src/routes/ProtectedRoute.tsx` (depends on T027)

**Checkpoint**: User Story 1 is independently functional — the blocking consent banner
works end-to-end. (Its Privacy-Page link resolves once US3/T042 adds the `/privacy`
route — see Dependencies below.)

---

## Phase 4: User Story 2 - Manage Cookie Preferences from Settings (Priority: P2)

**Goal**: An authenticated user can view their current per-category consent, see when it
was last updated, and change/save it at any time from Settings, with the change taking
effect immediately.

**Independent Test**: As a user with an existing consent decision, open Settings >
Cookies, change a toggle, save, and confirm the change is reflected immediately and
persists across sessions (quickstart.md Scenario 3).

### Tests for User Story 2

- [X] T029 [US2] Playwright E2E: Settings > Cookies displays current preferences and last-
  updated timestamp; toggling and saving updates immediately and refreshes the timestamp;
  a simulated save failure shows a visible error and leaves prior preferences in effect
  (quickstart.md Scenario 3) in `tests/AskLucy.E2E.Tests/CookiePreferencesSettings.spec.ts` —
  not runnable in this sandbox (same documented limitation as every other E2E spec);
  verified via `playwright test --list` that all 3 scenarios parse correctly

  > No new backend tests are needed for this story — it reuses the exact `GET`/`PUT
  > api/v1/users/me/cookie-consent` endpoints already covered by T013/T014/T017.

### Implementation for User Story 2

- [X] T030 [US2] Create `CookiePreferencesPanel.tsx` — reads `useCookieConsent()`, renders
  a toggle per category (sourced from `cookieCategories.ts`, Essential shown locked-on),
  the last-updated timestamp, a Save button wired to the mutation, a visible `Alert` on
  save/load failure (constitution §2.VIII, matching `SettingsPage.tsx`'s existing
  `changePassword.isError` idiom), and a link to `/privacy` (FR-010/SC-006 — functional
  once T042 adds the route, same one-way dependency as T026's banner link) in
  `src/AskLucy.Web/ClientApp/src/features/consent/components/CookiePreferencesPanel.tsx`
  (depends on T025, T025a) — the editable toggle state is owned by an inner
  `CookiePreferencesForm`, `key`ed by `lastUpdatedAtUtc` so a successful save remounts it
  with fresh initial state, rather than syncing local state from the query via a
  `useEffect` (React's `react-hooks/set-state-in-effect` lint rule flags that pattern)
- [X] T031 [US2] Add a fourth "Cookies" `Tab`/`TabPanel` to `SettingsPage.tsx` rendering
  `CookiePreferencesPanel` in
  `src/AskLucy.Web/ClientApp/src/features/settings/pages/SettingsPage.tsx` (depends on
  T030)

**Checkpoint**: User Stories 1 AND 2 both work independently.

---

## Phase 5: User Story 3 - Privacy Page Disclosure (Priority: P3)

**Goal**: Anyone — logged in or not — can open a public Privacy Page from the banner,
Settings, or the app's footer, describing cookie categories, data practices, third
parties, retention, and the current policy version.

**Independent Test**: Navigate to the Privacy Page without logging in and confirm it
renders with cookie category descriptions, third-party disclosures, retention
information, and a working link to manage preferences (quickstart.md Scenario 4).

### Tests for User Story 3

- [X] T032 [P] [US3] Unit tests for `GetCookiePolicyQueryHandler` — returns the current
  version/effective date from `ICookiePolicyProvider`, no user context required in
  `tests/AskLucy.Application.Tests/Consent/GetCookiePolicyQueryHandlerTests.cs`
- [X] T033 [P] [US3] Controller test: `GET /api/v1/cookie-policy` returns `200` with **no**
  `Authorization` header (confirms `[AllowAnonymous]` truly requires no auth) in
  `tests/AskLucy.Web.Tests/Consent/CookiePolicyEndpointTests.cs`
- [X] T034 [US3] Playwright E2E: Privacy Page loads pre-login with full content and the
  live policy version; reachable in one click from the banner, the footer, and Settings;
  a simulated policy-version bump re-triggers the banner for a previously-consented user
  (quickstart.md Scenario 4 & 5) in `tests/AskLucy.E2E.Tests/PrivacyPage.spec.ts` — not
  runnable in this sandbox (same documented limitation as every other E2E spec); verified
  via `playwright test --list` that all 3 scenarios parse correctly; the "footer" leg of
  the reachability scenario exercises `UserMenu`'s Privacy item for the authenticated path
  per T040/T041's note (footer proper covers the Privacy Page itself and the public
  auth pages)

### Implementation for User Story 3

- [X] T035 [P] [US3] Create `CookiePolicyDto` (`Version`, `EffectiveAtUtc`) in
  `src/AskLucy.Application/Consent/CookiePolicyDto.cs`
- [X] T036 [US3] Create `GetCookiePolicyQuery`/`GetCookiePolicyQueryHandler` — reads
  `ICookiePolicyProvider.GetCurrentPolicy()`, no current-user dependency in
  `src/AskLucy.Application/Consent/Queries/GetCookiePolicy/GetCookiePolicyQuery.cs` (+
  Handler) (depends on T004, T035, T032 failing first)
- [X] T037 [US3] Extend `CookieConsentController` — add `[AllowAnonymous] GET
  api/v1/cookie-policy` returning `CookiePolicyResponse` (contracts/cookie-consent-api.md)
  in `src/AskLucy.Web/Controllers/v1/CookieConsentController.cs` (depends on T023, T036,
  T033 failing first)
- [X] T038 [P] [US3] Create `privacyApi.ts` — `getCookiePolicy()` in
  `src/AskLucy.Web/ClientApp/src/features/privacy/api/privacyApi.ts` — also added a small
  `useCookiePolicy.ts` query hook alongside it (`features/privacy/hooks/`), matching every
  other feature's api+hook pairing convention
- [X] T039 [US3] Create `PrivacyPage.tsx` — static English-only copy (cookie categories/
  purposes sourced from `cookieCategories.ts` — not re-hardcoded, FR-021 — plus data
  collected, third-party services, retention, FR-009) plus the live version/effective
  date from `privacyApi`, and a "manage your preferences" link (to Settings > Cookies when
  authenticated, to /login when not) in
  `src/AskLucy.Web/ClientApp/src/features/privacy/pages/PrivacyPage.tsx` (depends on T038,
  T025a)
- [X] T040 [US3] Create `AppFooter.tsx` — new shared component with a Privacy link,
  justified by ≥2 usage sites (constitution §7) in
  `src/AskLucy.Web/ClientApp/src/components/AppFooter.tsx`
- [X] T041 [US3] Render `AppFooter` — **adjusted from the original plan during
  implementation**: `ChatPage.tsx` turned out to be a full-height (`100vh`) flex chat
  layout with no footer region (would require a larger layout redesign, out of scope).
  Rendered `AppFooter` in `AuthLayout.tsx` instead (covers login/register/etc. — pre-login
  discoverability, arguably a bigger real win than the authenticated shell) and in
  `PrivacyPage.tsx` itself, giving the required ≥2 usage sites (constitution §7). For the
  authenticated "global navigation" reachability FR-010 actually asks for, added a
  "Privacy Policy" `MenuItem` to `UserMenu.tsx` (already rendered in every authenticated
  page's `AppBar`, `ChatPage.tsx`) instead of a footer bar (depends on T040, T039)
- [X] T042 [US3] Add the public `/privacy` route, **outside** `ProtectedRoute`, to
  `src/AskLucy.Web/ClientApp/src/routes/router.tsx` (depends on T039) — this is what makes
  User Story 1's banner Privacy link (T026) and User Story 2's Cookie Preferences Privacy
  link (T030) resolve to a real page

**Checkpoint**: All three user stories are independently functional — full feature
complete.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements spanning multiple stories; final constitution/spec conformance
pass.

- [X] T043 [P] Add structured Serilog logging (Information level, security-relevant event
  per constitution §8) for every consent save — user id, prior/new category values, and
  policy version, never raw request bodies beyond those fields — in
  `src/AskLucy.Application/Consent/Commands/SaveMyCookieConsent/SaveMyCookieConsentCommandHandler.cs`
  — implemented back in T021, folded in while the handler was first written rather than
  deferred here
- [X] T044 [P] Confirm all 3 new endpoints are discoverable in the generated OpenAPI
  document with accurate request/response schemas (constitution §6) — run the app and
  inspect `/openapi/v1.json` — verified live: `dotnet run` (Development), fetched
  `/openapi/v1.json` (200, 72.5KB), confirmed all 3 paths
  (`GET/PUT /api/v1/users/me/cookie-consent`, `GET /api/v1/cookie-policy`) and their 3
  schemas (`CookieConsentResponse`, `SaveCookieConsentRequest`, `CookiePolicyResponse`,
  with the correct properties) are present; also confirmed live that `cookie-policy`
  returns `200` with no auth header and `cookie-consent` returns `401`
- [X] T045 [P] Run automated accessibility checks (axe) against `CookieConsentBanner`
  (focus handling on a non-dismissible `Dialog`), `CookiePreferencesPanel`, and
  `PrivacyPage` (constitution §7/§10) — added `CookieConsentBanner.a11y.test.tsx`,
  `CookiePreferencesPanel.a11y.test.tsx`, `PrivacyPage.a11y.test.tsx` (vitest + jest-axe +
  msw, matching `ChatSidebar.a11y.test.tsx`'s existing pattern); all pass (4/4 tests). The
  banner's Customize (per-category toggle) state isn't exercised — clicking Customize
  re-renders inside MUI Dialog's open transition, and any subsequent testing-library query
  then throws inside jsdom's CSS engine (`resolveLengthInPixels`), an environment
  limitation documented inline, matching this codebase's existing jsdom/virtualization gap
  precedent (`ChatSidebar.a11y.test.tsx`). The same `Switch`-based toggle UI is exercised
  successfully in `CookiePreferencesPanel.a11y.test.tsx` (no Dialog transition involved),
  so coverage isn't lost overall. **Found a pre-existing, unrelated test failure** while
  running the full frontend suite: `AdminUsersPage.test.tsx` (4 tests) fails even in
  complete isolation with "Cannot destructure property 'basename' of
  React.useContext(...) as it is null" (a missing Router-context wrapper) — confirmed via
  `git diff --stat` that neither that file, `AdminUsersPage.tsx`, nor `PageHeader.tsx` were
  touched by this feature; left as-is (out of scope, pre-existing, unrelated to cookie
  consent)
- [X] T046 [P] Document the "any future analytics/marketing integration must check
  `useCookieConsent()` before initializing" convention (research.md Topic 5, FR-019) in
  `docs/ARCHITECTURE.md` — added new §26 "Consent & Privacy Engine" (renumbering the old
  §26 Architecture Principles to §27)
- [X] T047 Run the full `quickstart.md` validation guide (all 6 scenarios) end-to-end
  against a fresh local environment and record results — not runnable in this sandbox (no
  live SQL Server/deployed frontend, consistent with every Playwright spec's own documented
  limitation). What *was* verified live in this environment: the full solution builds with
  zero errors; the `AddCookieConsent` migration applies its schema correctly (inspected,
  not applied — no reachable SQL Server instance here either); the app boots and serves
  `/openapi/v1.json` with all 3 endpoints (T044); `GET /api/v1/cookie-policy` returns `200`
  anonymously and `GET /api/v1/users/me/cookie-consent` returns `401` unauthenticated (both
  confirmed against the running app, not just unit tests). All backend test suites pass
  (213 tests: Domain 32, Application 96, Infrastructure 12, Web 73) except the
  Testcontainers-based Persistence suite (no Docker here). All frontend unit/a11y tests
  pass except the pre-existing, unrelated `AdminUsersPage.test.tsx` failure (T045's note).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup; **blocks every user story**.
- **User Stories (Phase 3–5)**: All depend on Foundational completion.
  - US1 (P1) has no dependency on US2 or US3's implementation work.
  - US2 (P2) reuses US1's `GET`/`PUT` endpoints and its `useCookieConsent` hook (T025)
    entirely — it adds only a new UI surface, no new backend work.
  - US3 (P3) depends on Foundational only for its own endpoint (`GetCookiePolicyQuery`),
    but US1's banner (T026) and US2's Cookie Preferences panel (T030) each contain a
    Privacy link that only resolves once US3 (T042) adds the `/privacy` route — a
    one-way UI dependency (link target), not a blocker on US1/US2's own independent
    testability (the banner/panel still block/save/persist correctly on their own; only
    the link's destination needs US3, matching quickstart.md's scenarios being separately
    runnable).
  - Recommended order: **US1 → US2 → US3** (matches spec.md priority order).
- **Polish (Phase 6)**: Depends on all desired user stories being complete.

### Parallel Opportunities

- All Foundational interface/entity/options tasks (T002, T003, T004, T005, T007a) are
  `[P]` — different files.
- Within US1, all test tasks (T012–T017) are `[P]`, `T024` (frontend API client) can
  proceed in parallel with backend tasks T019–T023, and `T025a` (shared category
  constants) can proceed in parallel with anything in T019–T025.
- Within US3, T032/T033/T035/T038 are `[P]`.
- All Polish tasks (T043–T046) are `[P]`.

---

## Parallel Example: User Story 1

```bash
# Tests together:
Task: "Unit tests for CookieConsentRecord.Create in tests/AskLucy.Domain.Tests/Consent/CookieConsentRecordTests.cs"
Task: "Unit tests for GetMyCookieConsentQueryHandler in tests/AskLucy.Application.Tests/Consent/GetMyCookieConsentQueryHandlerTests.cs"
Task: "Unit tests for SaveMyCookieConsentCommandHandler in tests/AskLucy.Application.Tests/Consent/SaveMyCookieConsentCommandHandlerTests.cs"
Task: "Integration test for UserCookieConsentRepository.GetLatestAsync in tests/AskLucy.Persistence.Tests/Consent/UserCookieConsentRepositoryTests.cs"
Task: "Unit test for CookiePolicyProvider in tests/AskLucy.Infrastructure.Tests/Consent/CookiePolicyProviderTests.cs"
Task: "Controller tests for CookieConsentController in tests/AskLucy.Web.Tests/Consent/CookieConsentControllerTests.cs"

# Frontend API client alongside backend implementation:
Task: "Create consentApi.ts in src/AskLucy.Web/ClientApp/src/features/consent/api/consentApi.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) + Phase 2 (Foundational).
2. Complete Phase 3 (US1) — the blocking, strict-opt-in consent banner.
3. **STOP and VALIDATE** against quickstart.md Scenario 1 & 2.
4. This alone satisfies the feature's core legal/compliance requirement even before
   Settings management or the Privacy Page content exist.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → validate → demo (MVP — strict opt-in enforced for every user).
3. US2 → validate → demo (ongoing self-service preference management).
4. US3 → validate → demo (public disclosure; completes the banner's Privacy link) — full
   feature complete.
5. Phase 6 (Polish) — cross-cutting hardening and doc updates.

---

## Notes

- `[P]` tasks touch different files with no unmet dependency.
- `CookieConsentRecord` is append-only by design (research.md Topic 2) — no task should
  add an `Update`/mutator method to it; every preference change is a new inserted row.
- Essential is never persisted as a toggleable value (data-model.md) — do not add an
  `EssentialAccepted` column or request field; it is a fixed `true` constant in every
  response.
- Commit after each task or logical group; stop at any Checkpoint to validate a story
  independently before moving on.
- Avoid: a second "consent history" table alongside the current-state table (research.md
  Topic 2 — `GetHistoryAsync` reads the *same* append-only table, T003/T009), a
  database-editable cookie-category admin screen (research.md Topic 4), or a placeholder
  analytics integration built solely to demonstrate FR-019 gating (research.md Topic 5).
- `GetHistoryAsync` (T003/T009) satisfies FR-016 as a data-access capability only; no
  admin/compliance UI or endpoint surfaces it in this feature — add one only if a future
  spec explicitly requests it (YAGNI).
- The Privacy link now appears in three places (banner T026, Cookie Preferences panel
  T030, footer T040/T041) — all three MUST import from `cookieCategories.ts` (T025a) and
  point at the same `/privacy` route (T042); do not let any of them hardcode category
  text independently (FR-021).
