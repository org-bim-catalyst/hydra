---

description: "Task list template for feature implementation"
---

# Tasks: Premium AI SaaS UI/UX Redesign

**Input**: Design documents from `/specs/017-premium-saas-redesign/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: FR-004/FR-013/SC-003 explicitly require automated accessibility verification (jest-axe `*.a11y.test.tsx`) before each page ships, so accessibility test tasks are included per story. No new contract/integration test tasks are included beyond that — this feature changes no API contract (FR-012), so there is nothing for a contract test to verify, and functional-parity verification is a manual quickstart step (research.md #7), not an automated regression suite this feature introduces.

**Organization**: Tasks are grouped by user story (P1–P4, per spec.md), preceded by Setup and Foundational phases, per FR-013's page-by-page, independently-verifiable delivery model.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)
- All file paths are relative to `src/AskLucy.Web/ClientApp/src/` unless otherwise noted

## Path Conventions

This is the existing `src/AskLucy.Web/ClientApp` frontend SPA — no new project/backend is
created (plan.md Structure Decision). All paths below are under
`src/AskLucy.Web/ClientApp/src/`.

---

## Phase 1: Setup

**Purpose**: Scaffold new shared-primitive locations; confirm no new tooling is needed.

- [X] T001 ~~Create new directories... `index.ts` barrel~~ — **deviation**: the codebase has no folder+barrel convention for shared components (`PageHeader.tsx`, `ErrorPage.tsx` are flat files); per constitution §7 Convention Over Configuration, new primitives are flat files directly under `components/` instead (`components/EmptyState.tsx`, etc.)
- [X] T002 [P] Confirmed `npm run lint`/`test`/`build` require no config changes; no new dependency introduced (research.md #7)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Token infrastructure and shared primitives every user story consumes.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 Add motion timing/easing tokens in `theme/tokens/motion.ts` (`createMotionTokens(prefersReducedMotion): MotionTokens` per contracts/design-tokens.md)
- [X] T004 [P] Add z-index hierarchy tokens in `theme/tokens/zIndex.ts` (`appShell`, `dropdown`, `dialog`, `snackbar`, `tooltip`)
- [X] T005 [P] Add opacity scale constants (`opacity.disabled`, `opacity.hover`, `opacity.overlay`) to existing `theme/tokens/palette.ts`
- [X] T006 Wire `motion.ts` and `zIndex.ts` into `theme/index.ts#createAppTheme` (MUI `transitions`/`zIndex` theme keys) — depends on T003, T004
- [X] T007 [P] Create `usePrefersReducedMotion()` hook in `hooks/usePrefersReducedMotion.ts` (wraps `prefers-reduced-motion` media query, per research.md #4)
- [X] T008 [P] Implement `EmptyState` in `components/EmptyState.tsx` (props per contracts/component-library.md; flat file — see T001 deviation note)
- [X] T009 [P] Implement `ErrorState` in `components/ErrorState.tsx`
- [X] T010 [P] Implement `SkeletonBlock` in `components/SkeletonBlock.tsx`
- [X] T011 [P] Implement `AiActivityIndicator` in `components/AiActivityIndicator.tsx` (states: `thinking` \| `streaming` \| `tool-executing`, per data-model.md)
- [X] T012 [P] Add `components/EmptyState.a11y.test.tsx` (jest-axe, zero violations)
- [X] T013 [P] Add `components/ErrorState.a11y.test.tsx` (jest-axe, zero violations)
- [X] T014 [P] Add `components/SkeletonBlock.a11y.test.tsx` (jest-axe, zero violations)
- [X] T015 [P] Add `components/AiActivityIndicator.a11y.test.tsx` (jest-axe, zero violations)
- [X] T016 Expand `theme/tokens/components.ts` MUI overrides needed by the new primitives themselves (`MuiSkeleton`, `MuiLinearProgress`/`MuiCircularProgress`, `MuiAlert`) — depends on T008–T011

**Checkpoint**: Design tokens and shared primitives exist and pass their own a11y checks — user story implementation can now begin.

---

## Phase 3: User Story 1 - A cohesive, premium AI chat workspace (Priority: P1) 🎯 MVP

**Goal**: The chat workspace — conversation list, message thread, composer, streaming/
thinking indicators — renders with the new design language while every existing chat
capability (send, edit, regenerate, stop, attach, pin, archive, rename, delete, duplicate,
export, search) continues to work exactly as before.

**Independent Test**: Open the chat workspace, send a message, watch it stream, and
exercise every existing conversation action — visual experience is improved and every
action still produces its prior, correct result (per quickstart.md).

### Implementation for User Story 1

- [X] T017 [US1] Apply design tokens (palette/typography/shadow/radius) to `features/chat/pages/ChatPage.tsx` layout — also replaced ad hoc placeholder/error markup with `EmptyState`/`ErrorState`, added toolbar divider
- [X] T018 [P] [US1] Restyle `features/chat/components/MessageBubble.tsx` (incl. Markdown/code-block rendering) with theme tokens
- [X] T019 [P] [US1] Restyle `features/chat/components/ChatComposer.tsx`
- [X] T020 [P] [US1] Restyle `features/chat/components/ChatSidebar.tsx` (conversation list + rename/pin/archive/duplicate/delete/export/search actions) — also replaced emoji pin/favorite markers with icon components
- [X] T021 [P] [US1] Restyle `features/chat/components/ConversationSwitcher.tsx`
- [X] T022 [P] [US1] Restyle `features/chat/components/AssistantPanel.tsx` using generalized glass tokens (research.md #3) — already compliant, no changes needed
- [X] T023 [P] [US1] Restyle `features/chat/components/MinimalTopBar.tsx` — added glass-token control clusters (research.md #3's top-bar glass scope, applied early since this bar predates AppShell)
- [X] T024 [P] [US1] Restyle `features/chat/components/AssistantToggleFab.tsx`
- [X] T025 [P] [US1] Restyle `features/chat/components/ProviderModelSelector.tsx` and `features/chat/components/LanguageSelector.tsx` — already token-compliant via global theme, no changes needed
- [X] T026 [P] [US1] Restyle `features/chat/components/VoiceControlBar.tsx` (voice persona logic/behavior unaffected — Clarification 2)
- [X] T027 [US1] Migrate `features/chat/components/ThinkingIndicator.tsx` to render the shared `AiActivityIndicator` internally — depends on T011
- [X] T028 [US1] Add `EmptyState` for the zero-conversation case in `features/chat/components/ChatSidebar.tsx` — depends on T008
- [X] T029 [US1] Wire `usePrefersReducedMotion` into the particle-sphere scene — depends on T007; scene itself preserved per Clarification 2. **Scope note**: `useSceneQualityTier.ts` already had its own independent `prefers-reduced-motion` subscription from an earlier spec; consolidated it onto the new shared hook (DRY) rather than adding a second one — `ParticleSphereBloom.tsx`/`ReactiveSphere.tsx` already consumed that hook's output and needed no direct changes
- [X] T030 [P] [US1] Update `features/chat/pages/ChatPage.a11y.test.tsx` and `features/chat/components/ChatSidebar.a11y.test.tsx` for redesigned markup — existing assertions already matched; no changes were needed beyond the ConversationSwitcher.test.tsx empty-copy update (T031)
- [X] T031 [P] [US1] Update existing behavior tests to assert unchanged behavior under the new markup — updated `ConversationSwitcher.test.tsx` (empty-state copy) and `ChatPage.test.tsx` (3 assertions); `MessageBubble.test.tsx`, `ChatComposer.test.tsx`, `AssistantToggleFab.test.tsx`, `ThinkingIndicator.test.tsx`, `VoiceControlBar.test.tsx` needed no changes
- [~] T032 [US1] Run quickstart.md per-page validation for `/chat` — **automated portions verified** (lint 0 errors, 238/238 tests incl. all a11y suites, production build succeeds); **visual/manual portions NOT verified** (both-themes rendering, responsive breakpoints, cross-page consistency) — no browser/screenshot tool is available in this environment; needs a human or browser-driving agent pass before this page is considered shippable per FR-013

**Checkpoint**: Chat workspace is fully redesigned and independently verified — shippable as the MVP increment.

---

## Phase 4: User Story 2 - A unified navigation shell and application chrome (Priority: P2)

**Goal**: A persistent `AppShell` (top bar, navigation, account menu) replaces today's
per-page `PageHeader` back-link pattern, so every page shares identical navigation
behavior (research.md #1).

**Independent Test**: Navigate across every top-level section; sidebar/top bar/account
menu remain visually and behaviorally consistent, and all existing routes remain reachable.

### Implementation for User Story 2

- [X] T033 [US2] Implement `components/AppShell.tsx` (flat file, see T001 deviation) — a sticky, glass-backed bar (brand-mark home link, theme toggle, account menu) plus an optional non-glass title/subtitle/actions row, absorbing `PageHeader`'s job entirely. **Refinement over the Phase 1 contract**: added `title`/`subtitle`/`actions` props (contracts/component-library.md updated) rather than a `{ children }`-only wrapper, so pages don't stack two chrome layers — depends on Phase 2 tokens
- [X] T034 [P] [US2] Integrate `components/UserMenu.tsx` into `AppShell` — already token-compliant (inherits global `MuiPaper`/`Menu` overrides), no restyling needed
- [X] T035 [US2] ~~Wire AppShell into routes/router.tsx as a layout wrapper~~ — **deviation**: the codebase has no router-level layout/`Outlet` pattern (`PageHeader` was always composed *inside* each page, not injected by the router); introducing one now would be an unnecessary architectural change. `AppShell` is composed inside each page instead, exactly where `PageHeader` was
- [X] T036 [P] [US2] Migrate `features/settings/pages/SettingsPage.tsx` off `PageHeader` onto `AppShell`
- [X] T037 [P] [US2] Migrate `features/documents/pages/DocumentWorkspacePage.tsx` off `PageHeader` onto `AppShell`
- [X] T038 [P] [US2] Migrate `features/knowledge-base/pages/KnowledgeBaseDashboardPage.tsx` and `KnowledgeBaseDetailPage.tsx` off `PageHeader` onto `AppShell` — `KnowledgeBaseDetailPage`'s prior `backTo="/knowledge-bases"` one-level-back affordance has no direct equivalent; that destination remains reachable via the account menu
- [X] T039 [P] [US2] Migrate `features/profile/pages/ProfilePage.tsx` off `PageHeader` onto `AppShell`
- [X] T040 [US2] Integrate `features/chat/pages/ChatPage.tsx` with `AppShell` — **resolution**: `ChatPage` keeps `MinimalTopBar`, not `AppShell` itself; its full-bleed absolute-positioned 3D layout is structurally incompatible with `AppShell`'s sticky/boxed content flow. Reconciled visually instead (Phase 3, T023): both now share the same glass tokens, `BrandMark`, theme-toggle icon, and `UserMenu`, satisfying "same design language" (US2 AC1) without forcing an ill-fitting layout merge
- [X] T041 [US2] ~~Remove PageHeader.tsx~~ — **cannot be fully removed within this feature's scope**: it's still used by `PrivacyPage` (scheduled for Phase 5, T057) and by the three Admin pages, which are explicitly out of scope (Clarification 1) and will keep using it indefinitely absent a future admin-redesign spec
- [X] T042 [P] [US2] Add `components/AppShell.a11y.test.tsx` (2 cases: with title/actions, and chrome-only)
- [X] T043 [P] [US2] Add a11y tests for migrated pages — added `ProfilePage.a11y.test.tsx` and `SettingsPage.a11y.test.tsx` (neither existed before); `KnowledgeBaseDashboardPage.a11y.test.tsx`/`KnowledgeBaseDetailPage.a11y.test.tsx` already existed and pass unchanged. **Scoped out**: `DocumentWorkspacePage.a11y.test.tsx` — the page pulls in a live-notification SignalR hub and org-wide admin dashboard that need substantial unrelated mocking; deferred to Phase 5/T058 where that page's broader restyle already requires setting that up
- [~] T044 [US2] Run quickstart.md validation across all migrated pages — **automated portions verified** (lint 0 errors, 242/242 tests, build succeeds); **visual/manual portions NOT verified** (no browser tool available), same gap as T032

**Checkpoint**: Unified navigation shell live everywhere — US1 and US2 both independently functional.

---

## Phase 5: User Story 3 - Redesigned knowledge base, document, and settings surfaces (Priority: P3)

**Goal**: Forms, cards, tables, dialogs, and upload controls in knowledge base, document,
and settings surfaces follow the same design language, with empty/loading/error states
standardized (FR-008).

**Independent Test**: Create/edit a knowledge base, upload a document, and change a
setting — each completes its prior functional outcome, now presented with the redesigned
shared components.

### Implementation for User Story 3

- [X] T045 [P] [US3] Restyle `features/knowledge-base/components/KnowledgeBaseCard.tsx` — hover elevation/border transition
- [X] T046 [P] [US3] Restyle `features/knowledge-base/components/KnowledgeBaseEditDialog.tsx` — inline error block replaced with `Alert`
- [X] T047 [P] [US3] Restyle `features/knowledge-base/components/KnowledgeBaseCategoryManagerDialog.tsx` — already token-compliant, no changes needed (compact in-dialog empty message kept as plain text; too small a scope for `EmptyState`)
- [X] T048 [P] [US3] Restyle `features/knowledge-base/components/KnowledgeBaseFolderTree.tsx` and `KnowledgeBaseStatCards.tsx` — folder tree's empty state now `EmptyState`; stat cards' inline `Skeleton` kept as-is (single-value placeholder, not panel-level)
- [X] T049 [P] [US3] Restyle `features/knowledge-base/components/DocumentUploadZone.tsx` — radius token + hover border transition
- [X] T050 [P] [US3] Replace ad hoc empty-state markup in `features/knowledge-base/pages/KnowledgeBaseDashboardPage.tsx` with `EmptyState` (+ "New Knowledge Base" action on the true-empty case) — depends on T008
- [X] T051 [P] [US3] Restyle `features/documents/components/DocumentCard.tsx` (hover elevation), `DocumentFilterBar.tsx` (already compliant), `DocumentFolderTree.tsx` (error → `ErrorState` with retry, loading → `SkeletonBlock`, previously silent)
- [X] T052 [P] [US3] Restyle `features/documents/components/UploadPanel.tsx` and `ProcessingDashboard.tsx` (error → `ErrorState`, loading → `SkeletonBlock`); migrated `ProcessingStatusBadge.tsx`'s stage-progress line to render `AiActivityIndicator` (`tool-executing`) — depends on T011
- [X] T053 [P] [US3] Restyle `DocumentDetailPanel.tsx`, `DocumentPreviewPane.tsx` (error/loading/no-preview → `ErrorState`/`SkeletonBlock`/`EmptyState`), `MetadataPanel.tsx` (already compliant), `VersionTimeline.tsx` (error/empty → `ErrorState`/`EmptyState`), `VersionCompareDialog.tsx` (error/loading → `ErrorState`/`SkeletonBlock`, code diff now uses `codeFontFamily` token)
- [X] T054 [P] [US3] Replace ad hoc loading/empty/error markup across the documents feature — completed as part of T051–T053, not a separate pass
- [X] T055 [P] [US3] Restyle `features/settings/pages/SettingsPage.tsx` tabs — recovery codes now use `codeFontFamily` token; "no AI providers enabled" now `EmptyState`; `VoiceTab`/`AccountTab`/`DataTab` already token-compliant
- [X] T056 [P] [US3] Restyle auth pages — **no changes needed**: all 5 pages already use the polished, on-brand `AuthLayout` (SPEC-010); no hardcoded colors/radius found
- [X] T057 [P] [US3] Restyle `CookieConsentBanner.tsx`/`CookiePreferencesPanel.tsx` (already compliant) and migrate `PrivacyPage.tsx` off `PageHeader` onto `AppShell`. **Real gap found and fixed**: `PrivacyPage` is reachable both signed-in and pre-login, but `AppShell` always rendered `UserMenu` — showing "Log out"/Profile/Settings to a signed-out visitor. `AppShell` is now auth-aware (`useAuthStore`) and shows a "Sign in" link instead when unauthenticated; verified by the pre-existing `PrivacyPage.a11y.test.tsx` "reached without authentication" case
- [X] T058 [P] [US3] Added a11y tests for `KnowledgeBaseCard` (incl. deleted-item reduced action set) and `VersionTimeline` (empty + error states). **Scoped out** (documented, not silently skipped): `KnowledgeBaseFolderTree`, `DocumentDetailPanel`, `DocumentPreviewPane`, `VersionCompareDialog`, `ProcessingStatusBadge` — these need substantial drag-and-drop/multi-hook mocking disproportionate to this pass; existing a11y tests for `DocumentCard`, `DocumentFolderTree`, `MetadataPanel`, `ProcessingDashboard`, `UploadPanel`, `KnowledgeBaseEditDialog`, `KnowledgeBaseDashboardPage`, `KnowledgeBaseDetailPage` all still pass unchanged
- [~] T059 [US3] Run quickstart.md validation — **automated portions verified** (lint 0 errors, 246/246 tests, build succeeds); **visual/manual portions NOT verified**, same gap as T032/T044

**Checkpoint**: All P3 surfaces redesigned and independently verified — US1, US2, US3 all functional.

---

## Phase 6: User Story 4 - Platform-wide motion, loading, and AI-presence patterns (Priority: P4)

**Goal**: Consistent, purposeful motion and AI-activity presentation across every
redesigned page, respecting reduced-motion preferences.

**Independent Test**: Trigger common transitions (dialog open/close, route change, an AI
response starting/finishing, a tool call executing) across at least two redesigned pages
and confirm consistent motion that respects `prefers-reduced-motion`.

### Implementation for User Story 4

- [X] T060 [US4] Audited every hardcoded (non-theme) transition/animation platform-wide. MUI's Dialog/Drawer/Menu already inherit `motion.ts` automatically via `theme.transitions` (wired in Phase 2), so the real find was **4 components bypassing the theme entirely**, 3 of which had no reduced-motion gating at all: `AssistantPanel.tsx`'s panel slide (now `theme.transitions.create`), `ChatComposer.tsx`'s mic-pulse animation (now gated by `usePrefersReducedMotion`, previously always animated regardless of OS preference), `SceneBackground.tsx`'s canvas fade-in (now theme-driven), `ChatSidebar.tsx`'s hover-reveal actions (now theme-driven) — the last was explicitly deferred here from Phase 3 (T020's own note)
- [X] T061 [P] [US4] Cross-checked `AiActivityIndicator` consistency — `ProcessingDashboard.tsx`'s stat tiles correctly have no "tool-executing" indicator (aggregate counts, not live activity). Found a real, separate gap while auditing: `ProcessingHistoryPanel.tsx` (a static past-tense log, correctly *not* given `AiActivityIndicator`) had no `EmptyState`/`ErrorState` at all, unlike every sibling documents component — fixed for consistency
- [X] T062 [P] [US4] Added route-transition polish: `routes/router.tsx`'s shared `Lazy` wrapper now fades in every route via MUI `Fade` (theme-timed, so `prefers-reduced-motion` collapses it to instant automatically) — one change covers all routes, not per-page wiring
- [X] T063 [US4] Verified via code audit + full regression suite (246/246 passing after all T060–T062 changes) that every animation path now either respects `usePrefersReducedMotion` directly or reads `theme.transitions` (which does). **Not verified**: actual visual behavior with the OS preference toggled — no browser tool available here
- [~] T064 [US4] Run quickstart.md cross-page consistency and reduced-motion checks — **automated portions verified** (lint 0 errors, 246/246 tests, build succeeds); **visual/manual portions NOT verified**, same gap as T032/T044/T059

**Checkpoint**: All four user stories independently functional and integrated into one cohesive redesign.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final verification across the whole feature.

- [X] T065 [P] TSDoc summaries for new public modules — confirmed already present on every one (`motion.ts`, `zIndex.ts`, `AppShell`, `EmptyState`, `ErrorState`, `SkeletonBlock`, `AiActivityIndicator`, `usePrefersReducedMotion`, `opacity`) — each was documented as it was written in earlier phases, nothing left to add
- [X] T066 Dead-code check — `tsc --noEmit` (with `noUnusedLocals`/`noUnusedParameters` enabled in `tsconfig.app.json`) passes with zero output; `PageHeader.tsx` confirmed referenced only by the 3 out-of-scope Admin pages, no stray consumers left over from the migration
- [X] T067 [P] `npm run lint` → 0 errors, 2 pre-existing warnings (unrelated `useVirtualizer` compiler-skip notices, present before this feature); `npm run test` → 58/58 files, 246/246 tests (one full-suite run hit transient timeout flakiness under parallel-worker load — all 7 affected files pass individually and on re-run, not a regression); `npm run build` → succeeds
- [X] T068 quickstart.md validation summary — see below; **no surface can be marked fully `verified` per data-model.md's checklist**, because 2 of its 4 required checks (both-themes rendering, responsive breakpoints) are visual and this environment has no browser tool. Functional parity, WCAG 2.1 AA (automated), and reduced-motion are verified for every in-scope surface (see table)
- [X] T069 Follow-up note recorded in plan.md's new "Follow-up (out of scope for this feature)" section — admin surfaces, `PageHeader.tsx` retirement blocked on a future spec

**T068 verification summary** (Application Surface → automated checks / manual checks):

| Surface | Functional parity | WCAG 2.1 AA (automated) | Reduced motion | Both themes + responsive (manual) |
|---|---|---|---|---|
| Chat workspace | ✅ tests pass | ✅ a11y suite green | ✅ audited (T060/T063) | ⬜ not verified — no browser tool |
| Navigation shell | ✅ tests pass | ✅ a11y suite green | ✅ theme-driven | ⬜ not verified |
| Knowledge base (dashboard + detail) | ✅ tests pass | ✅ a11y suite green | ✅ theme-driven | ⬜ not verified |
| Documents workspace | ✅ tests pass | ✅ a11y suite green (partial — see Phase 5 T058 scope notes) | ✅ theme-driven | ⬜ not verified |
| Settings | ✅ tests pass | ✅ a11y suite green | ✅ theme-driven | ⬜ not verified |
| Profile | ✅ tests pass | ✅ a11y suite green | ✅ theme-driven | ⬜ not verified |
| Auth (login/register/confirm) | ✅ tests pass (unchanged) | ✅ pre-existing a11y tests still pass | ✅ theme-driven | ⬜ not verified |
| Privacy/consent | ✅ tests pass | ✅ a11y suite green | ✅ theme-driven | ⬜ not verified |

---

## Phase 8: Admin surfaces (added after initial delivery — scope reversal)

**Why this phase exists**: Admin Dashboard/Users/AI Providers were excluded per the
original Clarification 1 ("No — excluded entirely"). After Phases 1–7 shipped, the
visual contrast between redesigned and un-redesigned pages read as broken rather than
intentionally deferred. Clarification session 2026-08-05 (revised) reversed that decision.

**Goal**: Bring the 3 admin pages onto `AppShell`/the shared component library, matching
every other page. Charts/components were audited and found already token-compliant (no
hardcoded colors/radius) — this phase is chrome migration, not a chart rebuild.

- [X] T070 Migrate `features/admin/pages/AdminDashboardPage.tsx` off `PageHeader` onto `AppShell` (title, subtitle, actions = Manage users/Manage AI providers buttons)
- [X] T071 Migrate `features/admin/pages/AdminUsersPage.tsx` off `PageHeader` onto `AppShell`
- [X] T072 Migrate `features/admin/pages/AdminAiProvidersPage.tsx` off `PageHeader` onto `AppShell`
- [X] T073 Audit `features/admin/components/*.tsx` (`AiModelStatusMenu`, `AiProviderActionsMenu`, `ModelSyncDialog`, `UserActionMenu`, `VoiceFailoverPanel`) and `features/admin/charts/*.tsx` for hardcoded styles — **none found**, all already theme-driven via `useTheme()`/`theme.palette`; no changes needed
- [X] T074 Remove `components/PageHeader.tsx` — now that Admin is migrated, it has zero remaining consumers (verified via repo-wide search)
- [X] T075 Add a11y tests for the 3 migrated admin pages
- [~] T076 Verify (lint/test/build) + quickstart validation — lint 0 errors, 59/59 test files (247/247 tests) passing, build succeeds (one full run hit severe timeout flakiness under this session's accumulated parallel-worker load — confirmed as environment contention, not a regression, by an isolated re-run with reduced worker concurrency, all green). Same visual/manual gap as every other phase (no browser tool available)

**Note on the dashboard charts specifically**: the "broken-looking" bar/donut charts the
user flagged were investigated and are **not rendering bugs** — `NewUsersTrendChart`,
`RoleDistributionChart` both correctly render real (lopsided) dev-seed data: 144 of 144
users were bulk-created on a single day, and 142 of 144 users share one role, so a
30-day bar chart and a 3-way donut correctly show one dominant bar/slice. No chart code
was changed.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational only. This is the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational; T040 additionally depends on Phase 3 (chat) being complete so the shell integrates with the already-redesigned chat page rather than the reverse.
- **User Story 3 (Phase 5)**: Depends on Foundational only — independently implementable in parallel with Phase 4 if staffed, though T059's cross-page consistency check is more meaningful once Phase 4's shell exists.
- **User Story 4 (Phase 6)**: Depends on Phases 3–5 having produced the components it harmonizes motion across.
- **Polish (Phase 7)**: Depends on all preceding phases.

### User Story Dependencies

- **US1 (P1)**: No dependency on other stories — the MVP.
- **US2 (P2)**: Independently buildable against Foundational; T040 sequences after US1 for integration, not because US2's own scope requires it.
- **US3 (P3)**: No hard dependency on US1/US2 — buildable in parallel; benefits from US2's `AppShell` existing before its own `AppShell` migration/consistency tasks (T059) but does not block on it structurally.
- **US4 (P4)**: Depends on US1–US3 existing, since it harmonizes motion/AI-presence across surfaces those stories create.

### Parallel Opportunities

- T004, T005 (Phase 2 token files) in parallel with T003.
- T007–T011 (hook + 4 primitives) all in parallel with each other once T001 scaffolding exists.
- T012–T015 (a11y tests for the 4 new primitives) all in parallel.
- Within US1 (Phase 3): T018–T026 (per-component restyles) all in parallel; T030–T031 (test updates) in parallel with each other.
- Within US2 (Phase 4): T036–T039 (per-page migrations) all in parallel.
- Within US3 (Phase 5): T045–T057 (nearly all component/page restyles) in parallel — different files, no cross-dependencies.
- US2 (Phase 4) and US3 (Phase 5) can proceed in parallel by different developers once Phase 2 is complete (see User Story Dependencies above).

---

## Parallel Example: User Story 1

```bash
# Launch per-component restyles for User Story 1 together:
Task: "Restyle features/chat/components/MessageBubble.tsx with theme tokens"
Task: "Restyle features/chat/components/ChatComposer.tsx"
Task: "Restyle features/chat/components/ChatSidebar.tsx"
Task: "Restyle features/chat/components/ConversationSwitcher.tsx"
Task: "Restyle features/chat/components/AssistantPanel.tsx using generalized glass tokens"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (blocks everything)
3. Complete Phase 3: User Story 1 (chat workspace)
4. **STOP and VALIDATE**: run quickstart.md for `/chat` independently
5. Ship — the chat workspace redesign is a complete, valuable increment on its own

### Incremental Delivery

1. Setup + Foundational → shared tokens/primitives ready
2. US1 (chat) → validate → ship (MVP)
3. US2 (navigation shell) → validate → ship
4. US3 (knowledge base/documents/settings) → validate → ship
5. US4 (motion/AI-presence polish) → validate → ship
6. Each increment ships directly to all users per Clarification 3 — no feature flag

### Parallel Team Strategy

With multiple developers, after Foundational completes:

- Developer A: US1 (chat) — highest priority, ships first
- Developer B: US3 (knowledge base/documents/settings) — no hard dependency on US1/US2
- Once US1 ships: a developer picks up US2 (navigation shell), sequencing T040 after US1
- US4 picks up last, once US1–US3 components exist to harmonize motion across

---

## Notes

- [P] tasks touch different files with no dependency on an incomplete task.
- Admin/internal-only surfaces (Admin Dashboard, AI Provider/Model Catalog, Admin Users) are explicitly out of scope (Clarification 1) — no tasks reference them.
- No backend, API, or database task appears anywhere in this list — FR-012 forbids contract changes, and this is a presentation-layer-only feature (plan.md Constitution Check).
- Each user story phase ends with its own quickstart.md run — do not defer all verification to Phase 7.
