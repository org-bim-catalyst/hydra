---

description: "Task list for Flumeria Public Landing Experience"
---

# Tasks: Flumeria Public Landing Experience

**Input**: Design documents from `specs/023-flumeria-landing-experience/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The feature spec doesn't itself demand TDD, but the project constitution (`.specify/memory/constitution.md` §10/§16/§19) makes tests for new/changed behavior non-negotiable project-wide, so every story phase below includes them.

**Organization**: Tasks are grouped by user story (US1/US2/US3, matching spec.md's priorities) so each can be implemented and validated independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Maps the task to US1, US2, or US3 from spec.md
- File paths are exact, per plan.md's Project Structure

## Path Conventions

Existing layered solution, reused as-is (plan.md "Structure Decision"):
- Frontend: `src/AskLucy.Web/ClientApp/src/`
- Backend: `src/AskLucy.Application/`, `src/AskLucy.Web/`
- Tests: `tests/AskLucy.Application.Tests/`, `tests/AskLucy.Web.Tests/`, `tests/AskLucy.E2E.Tests/`, and frontend `*.test.tsx`/`*.a11y.test.tsx` co-located with source

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Scaffold the new feature areas; confirm no new dependency is required (plan.md Technical Context — this feature introduces zero new npm/NuGet packages).

- [X] T001 Create frontend feature folder skeletons: `src/AskLucy.Web/ClientApp/src/features/landing/{pages,components,content}/` and `src/AskLucy.Web/ClientApp/src/features/analytics/{hooks,api}/`
- [X] T002 [P] Create backend feature folder skeleton: `src/AskLucy.Application/Analytics/Commands/RecordFunnelEvent/`
- [X] T003 [P] Confirm baseline `npm run build` (in `src/AskLucy.Web/ClientApp`) and `dotnet build` succeed unchanged on the feature branch before any code is added, so later failures are attributable to this feature's changes only

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared infrastructure every user story depends on — routing gate, anonymous-safe consent mechanism, funnel-analytics endpoint/hook, and the shared auth-page layout update. No user story can be completed without this phase.

**⚠️ CRITICAL**: Complete this phase before starting any US1/US2/US3 task.

- [X] T004 [P] Create `PublicOnlyRoute` component (inverse of `ProtectedRoute`: renders children when signed out, `<Navigate to="/chat" replace />` when signed in) in `src/AskLucy.Web/ClientApp/src/routes/PublicOnlyRoute.tsx`, per contracts/routing-and-consent-contract.md
- [X] T005 [P] Create a minimal `LandingPage` shell (page component with no content sections yet, just enough to render as a route target) in `src/AskLucy.Web/ClientApp/src/features/landing/pages/LandingPage.tsx`
- [X] T006 Wire the router's root route: replace the unconditional `<Navigate to="/chat" replace />` at `/` with `<PublicOnlyRoute><LandingPage /></PublicOnlyRoute>` using the existing `<Lazy>`/`React.lazy` pattern in `src/AskLucy.Web/ClientApp/src/routes/router.tsx` (depends on T004, T005)
- [X] T007 [P] Create `usePublicCookieConsent` hook (reads/writes the `flumeria_public_consent` first-party cookie per data-model.md's `PublicConsentState` shape — no API call) in `src/AskLucy.Web/ClientApp/src/features/consent/hooks/usePublicCookieConsent.ts`
- [X] T008 [P] Create `PublicConsentBanner` component reusing the existing `COOKIE_CATEGORIES` taxonomy (`src/AskLucy.Web/ClientApp/src/features/consent/cookieCategories.ts`) for label/category consistency with the authenticated banner in `src/AskLucy.Web/ClientApp/src/features/consent/components/PublicConsentBanner.tsx`
- [X] T009 Create `PublicConsentGate` component (non-blocking-of-content wrapper per contracts/routing-and-consent-contract.md) in `src/AskLucy.Web/ClientApp/src/features/consent/components/PublicConsentGate.tsx` (depends on T007, T008)
- [X] T010 [P] Create `RecordFunnelEventCommand` and `RecordFunnelEventCommandValidator` (closed-enum `EventType`/`CtaId`/`FunnelType`, GUID `SessionId`, bounded `OccurredAtUtc` per data-model.md) in `src/AskLucy.Application/Analytics/Commands/RecordFunnelEvent/RecordFunnelEventCommand.cs` and `RecordFunnelEventCommandValidator.cs`
- [X] T011 Create `RecordFunnelEventCommandHandler` using the `[LoggerMessage]` source-generated `ILogger<RecordFunnelEventCommandHandler>` pattern (matching `SaveMyCookieConsentCommandHandler`'s convention) with named properties (no repository, no `DbContext` — research.md Topic 4) in `src/AskLucy.Application/Analytics/Commands/RecordFunnelEvent/RecordFunnelEventCommandHandler.cs` (depends on T010)
- [X] T012 Create `AnalyticsController` with `POST /api/v1/analytics/funnel-events`, `[AllowAnonymous]`, dispatching via `ISender` per contracts/analytics-funnel-events-api.md in `src/AskLucy.Web/Controllers/v1/AnalyticsController.cs` (depends on T011)
- [X] T013 Register a new named fixed-window rate-limit policy for the analytics endpoint, partitioned per client IP, following the existing pattern in `src/AskLucy.Web/Program.cs` (depends on T012)
- [X] T014 [P] Create `analyticsApi.ts` client (thin POST wrapper for the new endpoint) in `src/AskLucy.Web/ClientApp/src/features/analytics/api/analyticsApi.ts`
- [X] T015 Create `useFunnelAnalytics` hook exposing `recordCtaClick`/`recordFunnelCompleted` — consent-gated (checks `usePublicCookieConsent`), fire-and-forget, never blocks the caller (contracts/routing-and-consent-contract.md) in `src/AskLucy.Web/ClientApp/src/features/analytics/hooks/useFunnelAnalytics.ts` (depends on T007, T014)
- [X] T016 **Superseded and rebuilt twice**: originally a small wordmark-only tweak (research.md Topic 5), then a full rebuild once the actually-rendered reference showed a split-screen layout with an illustrated left panel + dark scrim + bottom-left brand/tagline overlay and a white right panel with green/gray-filled form styling — `AuthLayout` now matches that structure, cascading the Flumeria style to every form field/button in every consumer page via nested `sx` selectors (constitution §III DRY, no per-page changes needed). Lucy's portrait (spec 010-lucy-brand-refresh FR-011/013) is preserved, moved into the new overlay rather than dropped. `src/AskLucy.Web/ClientApp/src/components/AuthLayout.tsx`
- [X] T017 [P] Backend unit tests for `RecordFunnelEventCommandHandler` and `RecordFunnelEventCommandValidator` (mocked `ILogger<T>`, no database) in `tests/AskLucy.Application.Tests/Analytics/RecordFunnelEventCommandHandlerTests.cs` and `RecordFunnelEventCommandValidatorTests.cs` (depends on T010, T011)
- [X] T018 [P] Backend integration test for `AnalyticsController` — anonymous access succeeds, invalid payload returns `400` Problem Details — in `tests/AskLucy.Web.Tests/Analytics/AnalyticsControllerTests.cs` (depends on T012, T013). Burst/`429` behavior is verified manually via quickstart.md Scenario 7 / T054 rather than automated here, to avoid polluting the shared rate-limit partition state other tests in the same `CustomWebApplicationFactory`-backed class share.
- [X] T019 [P] Frontend unit test proving `useFunnelAnalytics` never calls the API before consent is granted, and that a failed call doesn't throw/block the caller, in `src/AskLucy.Web/ClientApp/src/features/analytics/hooks/useFunnelAnalytics.test.tsx` (depends on T015)
- [X] T020 [P] Frontend unit test for `PublicOnlyRoute` redirect behavior (signed-in → redirect, signed-out → renders children) in `src/AskLucy.Web/ClientApp/src/routes/PublicOnlyRoute.test.tsx` (depends on T004)

**Checkpoint**: Foundation ready — routing, consent, and analytics infrastructure exist; user story implementation can now begin.

---

## Phase 3: User Story 1 - Prospective user discovers Flumeria and creates an account (Priority: P1) 🎯 MVP

**Goal**: A signed-out visitor arrives at `/`, understands Flumeria's value from the landing page content, and can sign up straight into the authenticated workspace.

**Independent Test**: Visit the public root URL as a signed-out visitor, read the page content, select "Create Account / Sign Up," complete sign-up, and confirm arrival in the authenticated workspace (spec.md US1 Independent Test).

### Implementation for User Story 1

- [X] T021 [P] [US1] Write landing page copy (what Flumeria is, problems solved, how AI-assisted urban design works, Lucy's role, GIS/maps/2D-3D/spatial-analysis integration, how AI-generated analysis is visualized, differentiation from conventional tools — FR-002) in `src/AskLucy.Web/ClientApp/src/features/landing/content/copy.ts`
- [X] T022 [P] [US1] **Superseded and rebuilt** (research.md Topic 3 course-correction): `LandingHero` now matches the actually-rendered Readdy.ai reference — full-bleed `SiteIllustration` background + dark scrim + minimal nav + eyebrow/headline/subhead — not the earlier CSS/SVG "drafting table" motif built before the reference was ever seen. `src/AskLucy.Web/ClientApp/src/features/landing/components/LandingHero.tsx`
- [X] T023-T027 **Superseded and replaced**: the original four sections (`ValuePropositionSection`, `SpatialIntegrationSection`, `AnalysisVisualizationSection`, `DifferentiationSection`) were deleted and replaced with the reference's actual structure — a reusable `FeatureBlock.tsx` component rendered six times (Site Intelligence, Urban Context, Design Analysis, AI Insights, Design Evaluation, Design Comparison) by `FeatureBlocksSection.tsx`, driven by a `featureBlocks` data array in `copy.ts` (constitution §III DRY — one component, not six near-duplicate files). Covers the same FR-002 topics (GIS/spatial integration, AI-analysis visualization, differentiation) inside this six-block structure instead of as separate sections, matching the reference.
- [X] T024 (renumbered content) `HowItWorksSection` rebuilt as a 4-step horizontal timeline with a connecting line (Discover → Analyze → Evaluate → Design), matching the reference exactly, restyled green — `src/AskLucy.Web/ClientApp/src/features/landing/components/HowItWorksSection.tsx`
- [X] T028 [US1] Build `LandingCtaBar` component rendering all three primary CTAs (Sign In, Create Account/Sign Up, Try the Platform per FR-003), each wired to call `useFunnelAnalytics().recordCtaClick` in `src/AskLucy.Web/ClientApp/src/features/landing/components/LandingCtaBar.tsx` (depends on T015). Also implements the auth-aware "Try the Platform" branch (T045) and Sign In navigation (T038) in the same component, since all three CTAs live in one file — see notes on those tasks.
- [X] T029 [US1] Assemble the full `LandingPage`: Hero → HowItWorks → six FeatureBlocks → `StatsSection` (green band, new) → `NewsletterSection` (black band, presentational-only, new) → `LandingFooter` (black, new, landing-only — not the shared `AppFooter`), wrap in `PublicConsentGate`, SEO tags via React 19 native hoisting (research.md Topic 1, FR-022) in `src/AskLucy.Web/ClientApp/src/features/landing/pages/LandingPage.tsx` (depends on T021-T028, T009). Structure matches the reference design's actual section order (research.md Topic 3 course-correction) rather than the originally planned ad hoc structure.
- [X] T030 [US1] Wire the "Create Account / Sign Up" CTA in `LandingCtaBar` to navigate to `/register` (depends on T028)
- [X] T031 [US1] Restyle `RegisterPage` using the rebuilt `AuthLayout` (split-screen: illustrated left panel with dark scrim + brand/tagline overlay + Lucy's portrait preserved from spec 010; white right panel with green/gray-filled form styling cascaded from `AuthLayout`, matching the reference's sign-up page), wrap in `PublicConsentGate` in `src/AskLucy.Web/ClientApp/src/features/auth/pages/RegisterPage.tsx` (depends on T009, T016)
- [X] T032 [US1] Call `useFunnelAnalytics().recordFunnelCompleted('SignUp')` immediately after a successful registration response, at the point the confirmation-pending state is shown — registration issues no session and does not redirect (spec.md FR-008, Clarifications) — in `RegisterPage.tsx` (depends on T015, T031)
- [X] T033 [P] [US1] Frontend component + accessibility tests for `LandingPage` in `src/AskLucy.Web/ClientApp/src/features/landing/pages/LandingPage.test.tsx` and `LandingPage.a11y.test.tsx` (depends on T029)
- [X] T034 [P] [US1] Frontend test for the restyled `RegisterPage` (visual consistency with landing page, funnel-completed event fires on success) in `src/AskLucy.Web/ClientApp/src/features/auth/pages/RegisterPage.test.tsx` (depends on T031, T032)
- [X] T034a [P] [US1] Frontend test simulating a network/server failure during sign-up (via msw), asserting a visible error message and no stuck/silent state — FR-017, edge case spec.md:76 — in `src/AskLucy.Web/ClientApp/src/features/auth/pages/RegisterPage.test.tsx` (depends on T031)
- [X] T035 [US1] Playwright E2E: landing → sign-up form submission → branded confirmation-pending state (no redirect asserted, since registration issues no session) in `tests/AskLucy.E2E.Tests/LandingToSignup.spec.ts` (PascalCase filename, matching this test project's existing convention) (depends on T029, T030, T031, T032). Not runnable in this environment (no live deployment), matching every other spec in this suite.

**Checkpoint**: User Story 1 is fully functional and independently testable — this is the MVP.

---

## Phase 4: User Story 2 - Returning user signs in and resumes work (Priority: P2)

**Goal**: A returning, signed-out visitor can reach sign-in from the landing page (or directly via `/login`), authenticate, and land back in their workspace with existing behavior (errors, 2FA) unchanged.

**Independent Test**: Visit the public URL as a signed-out visitor with an existing account, select "Sign In," authenticate with valid credentials, and confirm arrival in the same authenticated workspace (spec.md US2 Independent Test).

### Implementation for User Story 2

- [X] T036 [US2] Restyle `LoginPage` using the extended `AuthLayout`, wrap in `PublicConsentGate` in `src/AskLucy.Web/ClientApp/src/features/auth/pages/LoginPage.tsx` (depends on T009, T016)
- [X] T037 [US2] Call `useFunnelAnalytics().recordFunnelCompleted('SignIn')` immediately before redirecting to `/chat` on successful sign-in, without delaying the redirect, in `LoginPage.tsx` (depends on T015, T036)
- [X] T038 [US2] Wire the "Sign In" CTA in `LandingCtaBar` to navigate to `/login` (depends on T028) — done as part of T028's single-file implementation
- [X] T039 [US2] Verify the existing invalid-credentials error message, two-factor-authentication challenge step, and social-login providers still render and function correctly post-restyle — no behavioral regression (FR-009, FR-017) — covered by `LoginPage.test.tsx`; the `ExternalLoginCompletePage` round-trip specifically is covered when that page is restyled in US3 (T044/T051) (depends on T036, T044)
- [X] T040 [P] [US2] Frontend test for restyled `LoginPage` (visual consistency, error states preserved, funnel-completed event fires) in `src/AskLucy.Web/ClientApp/src/features/auth/pages/LoginPage.test.tsx` (depends on T036, T037, T039)
- [X] T041 [US2] Playwright E2E: landing → sign-in → workspace journey in `tests/AskLucy.E2E.Tests/LandingToSignin.spec.ts` (PascalCase filename, matching convention) (depends on T036, T037, T038). Not runnable in this environment.

**Checkpoint**: User Stories 1 AND 2 both work independently.

---

## Phase 5: User Story 3 - Visitor experiences Flumeria and Ask Lucy as one coherent product (Priority: P3)

**Goal**: Across the entire journey — landing, every auth-flow page, "Try the Platform," and the moment of entry into the workspace — naming, visual identity, and routing behavior read as one coherent product.

**Independent Test**: Walk through the entire journey (landing → auth → workspace) as both a signed-out and a returning visitor and confirm consistent naming, visual identity, and messaging at each step, including workspace entry (spec.md US3 Independent Test).

### Implementation for User Story 3

- [X] T042 [P] [US3] Restyle `ConfirmEmailPage` using `AuthLayout`, wrap in `PublicConsentGate` in `src/AskLucy.Web/ClientApp/src/features/auth/pages/ConfirmEmailPage.tsx` (depends on T009, T016)
- [X] T043 [P] [US3] Restyle `ConfirmEmailChangePage` using `AuthLayout`, wrap in `PublicConsentGate` in `src/AskLucy.Web/ClientApp/src/features/auth/pages/ConfirmEmailChangePage.tsx` (depends on T009, T016)
- [X] T044 [P] [US3] Restyle `ExternalLoginCompletePage` using `AuthLayout`, wrap in `PublicConsentGate` in `src/AskLucy.Web/ClientApp/src/features/auth/pages/ExternalLoginCompletePage.tsx` (depends on T009, T016). Also records `recordFunnelCompleted('SignIn')` on a successful OAuth round-trip (FR-021 completeness beyond the literal task text — this page is a sign-in-completion path too).
- [X] T045 [US3] Implement "Try the Platform" CTA logic in `LandingCtaBar` — read `accessToken` from `authStore`; signed-out → navigate to `/register` and record the CTA click; signed-in → navigate directly to `/chat` (FR-006, US3 Scenarios 2–3) (depends on T028, T015) — done as part of T028's single-file implementation
- [X] T046 [US3] Review and align copy so "Lucy"/"Ask Lucy" is consistently introduced as the AI capability within Flumeria across `copy.ts` and `AuthLayout` (FR-010) in `src/AskLucy.Web/ClientApp/src/features/landing/content/copy.ts` and `src/AskLucy.Web/ClientApp/src/components/AuthLayout.tsx` (depends on T021, T016)
- [X] T047 [US3] Add the minimal Flumeria brand-transition element to the existing brand cluster (alongside `BrandMark` + "Ask Lucy" text) in `src/AskLucy.Web/ClientApp/src/features/chat/components/MinimalTopBar.tsx` (FR-011, research.md Topic 7)
- [X] T048 [P] [US3] Frontend tests for the three restyled pages from T042–T044 in their respective `*.test.tsx` files alongside each page
- [X] T049 [P] [US3] Frontend test for "Try the Platform" behavior in both auth states in `src/AskLucy.Web/ClientApp/src/features/landing/components/LandingCtaBar.test.tsx` (depends on T045)
- [X] T050 [P] [US3] Frontend test for the `MinimalTopBar` brand-transition element in `src/AskLucy.Web/ClientApp/src/features/chat/components/MinimalTopBar.test.tsx` (depends on T047)
- [X] T051 [US3] Playwright E2E: authenticated visitor hitting `/` redirects to `/chat`, and both "Try the Platform" branches, in `tests/AskLucy.E2E.Tests/PublicOnlyRouteAndTryPlatform.spec.ts` (PascalCase filename, matching convention) (depends on T006, T045). Not runnable in this environment.

**Checkpoint**: All user stories are now independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Cross-story verification against spec.md's Success Criteria and quickstart.md's scenarios.

- [ ] T052 [P] Full responsive/viewport sweep (small phone through ultra-wide desktop) across `LandingPage` and all five restyled auth pages — quickstart.md Scenario 1 step 4, FR-012/FR-013, SC-004. **Not completed in this environment** (no live browser available to this agent): every new component uses MUI's `xs`/`sm`/`md` responsive breakpoints throughout (structural compliance), and `LandingToSignup.spec.ts` includes an automated 360px-viewport assertion, but neither substitutes for an actual visual sweep. Needs a human/CI pass with a real browser before this checkbox is genuinely earned.
- [X] T053 [P] `jest-axe` accessibility sweep across `LandingPage` and all five restyled auth pages — quickstart.md Scenario 5, FR-016. Done: `LandingPage.a11y.test.tsx`, `LoginPage.a11y.test.tsx`, `RegisterPage.a11y.test.tsx` (pre-existing, still passing), `ConfirmEmailPage.test.tsx`, `ConfirmEmailChangePage.test.tsx`, `ExternalLoginCompletePage.test.tsx` — all pass with zero axe violations.
- [ ] T054 [P] Validate rate-limiting (`429`) and Problem Details (`400`) responses for `POST /api/v1/analytics/funnel-events` — quickstart.md Scenario 7. `400`/Problem Details validated automatically (`AnalyticsControllerTests.cs`); `429` burst behavior intentionally not automated (see T018's note) and **not manually verified in this environment** — needs a real run against a live instance.
- [ ] T055 [P] Validate Open Graph/social-preview rendering for the landing page against a standard OG validator — quickstart.md Scenario 6, SC-008. **Not completed in this environment** (no live deployed URL to validate against): the `og:title`/`og:description`/`og:type` tags are implemented and their presence is asserted in `LandingPage.test.tsx`, but an external OG-preview validator run is still needed once deployed.
- [X] T056 [P] Add XML doc / TSDoc summaries to new public surfaces (`AnalyticsController`, `RecordFunnelEventCommand`, `useFunnelAnalytics`, `PublicOnlyRoute`) per constitution §4 — done inline while writing each file.
- [ ] T057 Run the full quickstart.md validation end-to-end (all scenarios) and record results. **Not completed in this environment** (no simultaneously-running live frontend + backend + DB available to this agent for interactive browsing) — automated tests cover the equivalent assertions for Scenarios 1–5 and 7 (see T033/T034/T040/T048/T053/AnalyticsControllerTests), but an actual interactive run against a live instance is still needed, especially for Scenarios 6 and 8 (OG preview, throttled-network trace).
- [ ] T057a [P] Measure that landing-page CTAs and above-the-fold content are interactive without waiting on anything heavy (e.g., Lighthouse/DevTools Performance trace on a throttled connection) — plan.md Performance Goals, constitution §15. **Not completed in this environment** (no browser/Lighthouse available), but structurally verified: `LandingHero` uses no three.js/WebGL (research.md Topic 3 refinement), and the built `LandingPage` route chunk is ~12 KB (`npm run build` output), versus the ~900 KB `SceneBackground` chunk the authenticated workspace loads — the public route carries none of that weight. A live trace is still the authoritative check.
- [X] T058 Regression smoke-check: confirm existing critical journeys (chat, knowledge base, admin) are unaffected by the `router.tsx`/`AuthLayout`/`MinimalTopBar` changes. Done via automated regression: full frontend suite (297/297 passing across 88 files, including `ChatPage.test.tsx`/`ChatPage.a11y.test.tsx`) and `AskLucy.Web.Tests`/`AskLucy.Application.Tests`/`AskLucy.Domain.Tests`/`AskLucy.Infrastructure.Tests` all pass clean. `AskLucy.Persistence.Tests` fails uniformly (48/48, ~1ms each) in this sandbox — pre-existing "no real SQL Server instance reachable here" environment limitation (none of the 48 touch a file this feature changed; this feature adds zero Persistence-layer code by design, research.md Topic 4), not a regression. Needs re-verification in an environment with a real DB connection.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **User Stories (Phase 3–5)**: All depend on Foundational completion.
  - US1 (P1) has no dependency on US2/US3 and is the MVP.
  - US2 (P2) reuses `LandingCtaBar` (built in US1, T028) for wiring its own CTA (T038) and can otherwise proceed independently — it remains independently testable via direct `/login` navigation even before US1's CTA wiring lands.
  - US3 (P3) reuses `LandingCtaBar` (T028) and `AuthLayout`/copy (T016, T021) from US1, plus depends on `PublicOnlyRoute` (T006) from Foundational for its redirect verification — otherwise independent.
- **Polish (Phase 6)**: Depends on all three user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: Foundational only. No dependency on US2/US3.
- **User Story 2 (P2)**: Foundational + reuses the `LandingCtaBar` component US1 creates (T028) for T038's CTA wiring; all other US2 tasks (T036, T037, T039–T041) are independent of US1's completion.
- **User Story 3 (P3)**: Foundational + reuses `LandingCtaBar` (T028), `AuthLayout`/copy (T016, T021) from US1; independent otherwise.

### Within Each User Story

- Content/component tasks marked [P] before the assembly task that composes them.
- Page restyle before the funnel-event wiring that lives inside that page's success handler.
- Implementation before its own tests.
- Story complete (including its tests) before moving to the next priority.

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel.
- Within Foundational: T004/T005 (routing) can run parallel to T007/T008 (consent) and T010/T014 (analytics scaffolding); T017–T020 (tests) run parallel to each other once their respective implementation tasks land.
- Within US1: T021–T027 (all landing content sections) can run fully in parallel — different files, no shared state.
- Within US3: T042–T044 (the three secondary auth-page restyles) can run fully in parallel.
- Different user stories can be worked on in parallel by different contributors once Foundational is done, with the noted `LandingCtaBar`/`AuthLayout` reuse points synchronized.

---

## Parallel Example: User Story 1

```bash
# Launch all landing content sections together (different files, no dependencies):
Task: "Build ValuePropositionSection component in src/AskLucy.Web/ClientApp/src/features/landing/components/ValuePropositionSection.tsx"
Task: "Build HowItWorksSection component in src/AskLucy.Web/ClientApp/src/features/landing/components/HowItWorksSection.tsx"
Task: "Build SpatialIntegrationSection component in src/AskLucy.Web/ClientApp/src/features/landing/components/SpatialIntegrationSection.tsx"
Task: "Build AnalysisVisualizationSection component in src/AskLucy.Web/ClientApp/src/features/landing/components/AnalysisVisualizationSection.tsx"
Task: "Build DifferentiationSection component in src/AskLucy.Web/ClientApp/src/features/landing/components/DifferentiationSection.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories).
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: run quickstart.md Scenario 1 independently.
5. Deploy/demo if ready — this alone delivers the full acquisition funnel (landing → sign-up → confirmation-pending state; workspace arrival happens once the visitor confirms by email and signs in, US2).

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. Add User Story 1 → validate via quickstart.md Scenario 1 → deploy/demo (MVP).
3. Add User Story 2 → validate via quickstart.md Scenario 2 → deploy/demo.
4. Add User Story 3 → validate via quickstart.md Scenarios 3–6 → deploy/demo.
5. Polish (Phase 6) → validate via quickstart.md Scenario 7 and the full run-through.

### Parallel Team Strategy

With multiple contributors:

1. Team completes Setup + Foundational together (routing/consent/analytics infrastructure is shared by every story).
2. Once Foundational is done:
   - Contributor A: User Story 1 (landing content + sign-up).
   - Contributor B: User Story 2 (sign-in), starting once T028 (`LandingCtaBar`) exists.
   - Contributor C: User Story 3 (secondary auth pages, brand copy pass, workspace header, Try the Platform), starting once T028/T016/T021 exist.
3. Stories complete and integrate independently; Polish (Phase 6) runs once all three land.

---

## Notes

- [P] tasks touch different files with no dependency on an incomplete task.
- [Story] labels trace every implementation/test task back to spec.md's US1/US2/US3.
- `LandingCtaBar` (T028) and the `AuthLayout` brand update (T016) are the only files more than one story's tasks touch — every other file is owned by exactly one story, preserving independence.
- No new npm/NuGet dependency, no EF Core migration, and no change to any existing Domain entity is required anywhere in this task list (plan.md, data-model.md).
- Commit after each task or logical group; stop at any checkpoint to validate a story independently before continuing.
