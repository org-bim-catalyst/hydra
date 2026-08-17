---

description: "Task list for AI-to-UI Floating Panel Framework (SPEC-028)"

---

# Tasks: AI-to-UI Floating Panel Framework

**Input**: Design documents from `/specs/028-ai-floating-panels/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included throughout — not optional here. Constitution §10/§18/§19 requires tests for new/changed
behavior in the same PR that introduces it; this is a governing project rule, not a per-feature choice.

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P2/P3/P3) to enable independent
implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Paths are relative to the repository root (`D:\Workshop\BIM Catalyst\Web Apps\Platform\Ask Lucy`)

## Path Conventions

Existing Clean Architecture + React SPA layout (see plan.md "Project Structure") — no new project:
- Backend: `src/AskLucy.Domain/`, `src/AskLucy.Application/`, `src/AskLucy.Infrastructure/`, `src/AskLucy.Persistence/`, `src/AskLucy.Web/`
- Frontend: `src/AskLucy.Web/ClientApp/src/`
- Tests: `tests/AskLucy.*.Tests/`, plus co-located `*.test.tsx`/`*.a11y.test.tsx` in `ClientApp/src`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the two new frontend dependencies this feature needs and scaffold empty package
directories so subsequent tasks have somewhere to land.

- [X] T001 Add `react-rnd` and `zod` to `src/AskLucy.Web/ClientApp/package.json` and run `npm install`
- [X] T002 [P] Scaffold empty `types/`, `store/`, `hooks/`, `components/` subfolders under `src/AskLucy.Web/ClientApp/src/viewer/panels/`
- [X] T003 [P] Scaffold empty `Panels/` folders under `src/AskLucy.Domain/`, `src/AskLucy.Application/` (`Queries/`, `Commands/`, `Abstractions/`), and `src/AskLucy.Infrastructure/` (`Persistence/`)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared panel framework shell (types, registry, store core, minimal chrome, mount
point) that every user story builds on. No story-specific transport (SignalR), interaction (drag/
resize), preference persistence, or viewer-context wiring is implemented yet — only the plumbing
they'll attach to.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 [P] Define `FloatingPanel`, `PanelTypeDefinition`, `PanelRequest`, `ViewerContextAssociation` types and the `MAX_CONCURRENT_PANELS = 10` constant per data-model.md in `src/AskLucy.Web/ClientApp/src/viewer/panels/types/panel.ts`
- [X] T005 Implement `PanelTypeRegistry` (`register`/`resolve`, dev-time duplicate-`typeKey` throw) per contracts/panel-type-registry.md in `src/AskLucy.Web/ClientApp/src/viewer/panels/registry.ts` (depends on T004)
- [X] T006 [P] Unit test `PanelTypeRegistry` register/resolve/duplicate-key behavior in `src/AskLucy.Web/ClientApp/src/viewer/panels/registry.test.ts`
- [X] T007 Implement `floatingPanelStore.ts` core state + `openPanel` (registry resolve + zod schema validate → `validationStatus`), `closePanel`, `focusPanel` (`zOrder`/`lastFocusedAtUtc` bump), `minimizePanel`/`restorePanel`, `updatePosition`/`updateSize` actions, session-scoped (no `persist`, matches `workspaceOverlayStore` convention) per data-model.md, in `src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.ts` (depends on T004, T005)
- [X] T008 Implement cascade placement (FR-021 — offset from a fixed corner, wrapping before the opposite edge) for `openPanel` calls with no `position`, in `floatingPanelStore.ts` (depends on T007)
- [X] T009 Implement `MAX_CONCURRENT_PANELS` LRU eviction (FR-022 — closes the least-recently-focused panel via `lastFocusedAtUtc` when a new `openPanel` would exceed the cap) in `floatingPanelStore.ts` (depends on T007) — uses a monotonic-timestamp helper (`nextTimestamp()`) instead of raw `Date.now()` so a same-millisecond burst of opens/focuses (spec Edge Cases) can never tie and silently mis-evict
- [X] T010 [P] Unit test `openPanel` validation states (`valid`/`invalid`/`unknown-type`) in `src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.test.ts`
- [X] T011 [P] Unit test cascade placement offsets and wraparound in `floatingPanelStore.test.ts`
- [X] T012 [P] Unit test LRU eviction at `MAX_CONCURRENT_PANELS` in `floatingPanelStore.test.ts`
- [X] T013 Implement minimal `FloatingPanel.tsx` chrome (MUI `Box`, title bar with a close button, content area rendering the resolved renderer or the unknown-type/invalid-data fallback per `validationStatus`, absolutely positioned/sized from the store — no drag/resize/minimize yet, those are US2) in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.tsx` (depends on T007)
- [X] T013a Apply a hardcoded default semi-transparent background (85%, matching the eventual preference default) to `FloatingPanel.tsx`'s chrome (FR-010) — independent of `panelPreferencesStore`, which doesn't exist until US3 — in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.tsx` (depends on T013)
- [X] T014 Implement `FloatingPanelHost.tsx` (renders `floatingPanelStore`'s panels as a list of `FloatingPanel`) in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanelHost.tsx` (depends on T013) — also applies the I1 remediation: the host passes pointer events through except where a panel actually sits (FR-003)
- [X] T015 Mount `<FloatingPanelHost />` over the viewer in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx` (depends on T014)
- [X] T016 [P] Component test: `FloatingPanel` renders the unknown-type and invalid-data fallbacks distinctly (never blank), in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.test.tsx`
- [X] T017 [P] a11y test for `FloatingPanel`'s close button (keyboard operable, labeled) in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.a11y.test.tsx`

**Checkpoint**: Foundation ready — panels can be opened programmatically, render with correct
fallback/valid states, and appear over the viewer without obstructing it. User story implementation
can now begin.

---

## Phase 3: User Story 1 - AI Presents a Visual Response as a Floating Panel (Priority: P1) 🎯 MVP

**Goal**: An AI response carrying visual data reaches the browser over a dedicated, per-user real-time
channel and appears as a floating panel over the viewer, using a registered panel type — including
registered-but-new types and unknown/invalid ones, all producing a visible outcome.

**Independent Test**: Trigger an AI response with visual content (or, absent live agent wiring, call
`floatingPanelStore.openPanel(...)` directly per quickstart.md Scenario 1) and confirm a floating panel
appears over the viewer while the viewer remains visible and interactive (spec.md US1 Acceptance
Scenarios).

### Backend for User Story 1

- [X] T018 [P] [US1] `IPanelNotifier` interface (`PanelRequestedAsync(userId, PanelRequestDto)`) in `src/AskLucy.Application/Abstractions/IPanelNotifier.cs` (repo convention: all repository/notifier abstractions live in this flat folder, e.g. `IUserVoicePreferenceRepository.cs`/`IAgentExecutionNotifier.cs` — not per-feature `Abstractions` subfolders as originally planned)
- [X] T019 [P] [US1] `PanelRequestDto` per data-model.md / contracts/panel-hub-events.md in `src/AskLucy.Application/Panels/PanelRequestDto.cs`
- [X] T020 [US1] `PanelHub` mirroring `AgentExecutionHub` (`[Authorize]`, group by `ClaimTypes.NameIdentifier`, route `/hubs/panels`) per contracts/panel-hub-events.md, in `src/AskLucy.Infrastructure/Panels/PanelHub.cs` (depends on T018)
- [X] T021 [US1] `PanelNotifier` implementing `IPanelNotifier` via `IHubContext<PanelHub>` in `src/AskLucy.Infrastructure/Panels/PanelNotifier.cs` (depends on T018, T020)
- [X] T022 [US1] Map `PanelHub` (`app.MapHub<PanelHub>("/hubs/panels")`) in `src/AskLucy.Web/Program.cs` and register `IPanelNotifier → PanelNotifier` in `src/AskLucy.Infrastructure/DependencyInjection.cs` (depends on T021)
- [X] T023 [P] [US1] Unit test `PanelNotifier` invokes the correct user group/method on a mocked `IHubContext<PanelHub>` in `tests/AskLucy.Infrastructure.Tests/Panels/PanelNotifierTests.cs`

### Frontend for User Story 1

- [X] T024 [P] [US1] Implement `useFloatingPanelHub.ts` (connects to `/hubs/panels`, on `PanelRequested` calls `floatingPanelStore.openPanel`) mirroring `useAgentExecutionHub.ts`, in `src/AskLucy.Web/ClientApp/src/viewer/panels/hooks/useFloatingPanelHub.ts` (depends on T007)
- [X] T025 [US1] Mount `useFloatingPanelHub()` once per session in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx` (depends on T024, T015)
- [X] T026 [P] [US1] Implement the `chart` panel type (zod schema, `ChartPanelRenderer` using the existing `d3`-based charting approach) in `src/AskLucy.Web/ClientApp/src/viewer/panels/types/chart/ChartPanel.tsx`
- [X] T027 [P] [US1] Implement the `table` panel type (zod schema, `TablePanelRenderer` using MUI `Table`) in `src/AskLucy.Web/ClientApp/src/viewer/panels/types/table/TablePanel.tsx`
- [X] T028 [US1] Register the `chart` and `table` types (import-for-side-effect convention, matching `GoogleMapsGisLayer.ts`) in `src/AskLucy.Web/ClientApp/src/viewer/panels/types/index.ts` (depends on T026, T027)
- [X] T029 [US1] Import `viewer/panels/types/index.ts` once at viewer bootstrap so built-in types are always registered, in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx` (depends on T028)
- [X] T030 [P] [US1] Expose `floatingPanelStore`/`panelTypeRegistry` on `window` in development builds only (`import.meta.env.DEV` guard), mirroring spec 027's `viewerEngine` devtools exposure, in `ViewerSurface.tsx`
- [X] T031 [P] [US1] Test: `useFloatingPanelHub` dispatches a received `PanelRequested` payload into `floatingPanelStore.openPanel`, in `src/AskLucy.Web/ClientApp/src/viewer/panels/hooks/useFloatingPanelHub.test.ts`
- [X] T032 [P] [US1] Component tests: `chart`/`table` renderers render valid data and their schemas reject invalid data, in `ChartPanel.test.tsx` and `TablePanel.test.tsx`
- [X] T033 [P] [US1] New Playwright E2E spec `tests/AskLucy.E2E.Tests/AiFloatingPanels.spec.ts` asserting a panel opened via `floatingPanelStore.openPanel` (`page.evaluate`) appears over the viewer and the viewer stays interactive underneath (SC-008)
- [X] T034 [US1] Run quickstart.md Scenario 1 and Scenario 2 manually; fix any issues found — not runnable in this environment (no live authenticated deployment, matching the existing `ImmersiveViewerPlatform.spec.ts` precedent's own documented caveat); verified instead via the full automated suite (`dotnet build`, `dotnet test tests/AskLucy.Infrastructure.Tests --filter Panels`, `npx tsc -b`, `npx eslint`, `npx vitest run src/viewer/panels src/features/viewer` — all pass) plus manual code review against quickstart.md's exact steps

**Checkpoint**: User Story 1 is fully functional and independently testable/demoable — this is the
suggested MVP stopping point.

---

## Phase 4: User Story 2 - User Manages Panel Layout (Priority: P2)

**Goal**: Open panels can be dragged, resized where appropriate, minimized/restored, closed, and
brought to front on interaction — all without affecting the viewer or other panels.

**Independent Test**: Open two or more panels and independently drag, resize (where supported),
minimize, close, and focus each one, confirming the viewer and other panels are unaffected (spec.md
US2 Acceptance Scenarios).

### Implementation for User Story 2

- [X] T035 [US2] Wrap `FloatingPanel.tsx`'s chrome in `<Rnd>` (react-rnd), wiring drag (title bar as the drag handle) and resize (`enableResizing` driven by `panel.resizable`) to `floatingPanelStore.updatePosition`/`updateSize` (FR-004/FR-005), in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.tsx` (depends on T013, T007)
- [X] T035a [US2] Constrain panel dragging to the viewer surface bounds (`bounds="parent"` on `<Rnd>`) and, since a manual "reset position" button can itself become unreachable if a panel is fully off-screen, implemented instead as automatic re-clamping: `floatingPanelStore.clampToViewport` runs on every window resize (wired in `FloatingPanelHost.tsx`) and nudges any panel back within the current viewport (FR-018, Edge Cases: viewport resize), in `src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.ts` + `.../components/FloatingPanelHost.tsx` (depends on T035)
- [X] T035b [P] [US2] Unit test `clampToViewport` repositions an out-of-bounds panel back within given bounds and leaves an in-bounds panel untouched, in `src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.test.ts`
- [X] T036 [US2] Implement minimize/restore UI (collapse to a compact bar; restore to the prior size/position exactly) calling `floatingPanelStore.minimizePanel`/`restorePanel` (FR-006), in `FloatingPanel.tsx` (depends on T035)
- [X] T037 [US2] Wire focus-on-interaction (mousedown calls `floatingPanelStore.focusPanel`, applying the highest `zOrder`, which drives the panel's stacking/focus style) (FR-009), in `FloatingPanel.tsx` (depends on T035)
- [X] T038 [P] [US2] Implement the `parameters` panel type (zod schema, form-style `ParametersPanelRenderer` using React Hook Form, `resizable: false`) in `src/AskLucy.Web/ClientApp/src/viewer/panels/types/parameters/ParametersPanel.tsx`, plus its own registration test in `ParametersPanel.test.tsx` (schema validation + registry entry, matching the `chart`/`table` test convention)
- [X] T039 [US2] Register the `parameters` type in `src/AskLucy.Web/ClientApp/src/viewer/panels/types/index.ts` (depends on T038, T028)
- [X] T040 [US2] Confirm `FloatingPanel.tsx` hides resize handles entirely when `panel.resizable` is `false` (FR-005/US2-AS3) via `enableResizing={panel.resizable}` passed straight through to `<Rnd>`, in `FloatingPanel.tsx` (depends on T035, T039)
- [X] T041 [P] [US2] Component test: `Rnd`'s `onDragStop` callback updates `floatingPanelStore` position (react-rnd itself is mocked to capture and invoke the exact props `FloatingPanel` passes it — verifies our integration wiring, not react-rnd's own internals), in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.test.tsx`
- [X] T042 [P] [US2] Component test: `Rnd`'s `onResizeStop` callback updates size+position for a resizable panel; `enableResizing` is `false` for the fixed-size `parameters` panel, in `FloatingPanel.test.tsx`
- [X] T043 [P] [US2] Component test: minimize collapses to the compact-bar variant (no `Rnd` chrome) and restore returns to the exact prior size/position, in `FloatingPanel.test.tsx`
- [X] T044 [P] [US2] Component test: mousedown on a panel calls `focusPanel` with its id, in `FloatingPanel.test.tsx`
- [X] T045 [P] [US2] a11y test: minimize/restore/close controls (both panel states) are labeled, keyboard-focusable, and axe-clean, in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.a11y.test.tsx`
- [X] T046 [P] [US2] Extend `tests/AskLucy.E2E.Tests/AiFloatingPanels.spec.ts` covering drag, fixed-size (no resize handles), minimize/restore, close, and focus-reorder
- [X] T047 [US2] Run quickstart.md Scenario 3 and Scenario 4 manually; not runnable in this environment (same documented caveat as T034) — verified instead via `dotnet build`, `npx tsc -b`, `npx eslint`, and `npx vitest run` (all pass) plus manual code review against quickstart.md's exact steps

**Checkpoint**: User Stories 1 and 2 both work independently — panels appear and are fully manageable.

---

## Phase 5: User Story 3 - User Controls Panel Transparency (Priority: P3)

**Goal**: A bounded (40%–100%) opacity preference, set from a new Settings "Viewer" tab, persists
across sessions and applies live to every open panel.

**Independent Test**: Open the Settings "Viewer" tab, change the opacity control, and confirm open
(and subsequently opened) panels reflect the new opacity, including after a reload (spec.md US3
Acceptance Scenarios).

### Backend for User Story 3

- [X] T048 [P] [US3] `UserPanelPreference` entity (`Create`, `SetOpacityPercent` clamped `[40, 100]`, `CreatedAtUtc/By`, `ModifiedAtUtc/By`) per data-model.md in `src/AskLucy.Domain/Panels/UserPanelPreference.cs`
- [X] T049 [P] [US3] `IUserPanelPreferenceRepository` interface in `src/AskLucy.Application/Abstractions/IUserPanelPreferenceRepository.cs` (flat folder, matches `IUserVoicePreferenceRepository.cs`)
- [X] T050 [P] [US3] `UserPanelPreferenceDto` in `src/AskLucy.Application/Panels/UserPanelPreferenceDto.cs`
- [X] T051 [US3] `GetUserPanelPreferenceQuery` + `GetUserPanelPreferenceQueryHandler` (returns the default `{ OpacityPercent: 85 }` without creating a row when none exists, per contracts/panel-preferences-api.md) in `src/AskLucy.Application/Panels/Queries/GetUserPanelPreference/` (depends on T049, T050)
- [X] T052 [US3] `SaveUserPanelPreferenceCommand` + `SaveUserPanelPreferenceCommandHandler` (create-if-null then `SetOpacityPercent`, commits via `IUnitOfWork`) + `SaveUserPanelPreferenceCommandValidator` (`[40, 100]`, FluentValidation) in `src/AskLucy.Application/Panels/Commands/SaveUserPanelPreference/` (depends on T048, T049, T050)
- [X] T053 [P] [US3] Unit test `UserPanelPreferenceTests.cs` (clamp behavior, `Create` factory) in `tests/AskLucy.Domain.Tests/Panels/`
- [X] T054 [P] [US3] Unit test `GetUserPanelPreferenceQueryHandlerTests.cs` (default-when-missing, existing row) in `tests/AskLucy.Application.Tests/Panels/`
- [X] T055 [P] [US3] Unit test `SaveUserPanelPreferenceCommandHandlerTests.cs` (create-on-first-save, update-existing) in `tests/AskLucy.Application.Tests/Panels/`
- [X] T056 [P] [US3] Unit test `SaveUserPanelPreferenceCommandValidatorTests.cs` (rejects values below 40 and above 100) in `tests/AskLucy.Application.Tests/Panels/`
- [X] T057 [US3] `UserPanelPreferenceConfiguration` (EF Core Fluent API, unique index on `UserId`) in `src/AskLucy.Persistence/Configurations/UserPanelPreferenceConfiguration.cs` (repo convention: EF configs live in `AskLucy.Persistence`, matching `UserVoicePreferenceConfiguration.cs` — not `AskLucy.Infrastructure`) (depends on T048)
- [X] T058 [US3] `UserPanelPreferenceRepository` implementing `IUserPanelPreferenceRepository` in `src/AskLucy.Persistence/Repositories/UserPanelPreferenceRepository.cs` (depends on T049, T057)
- [X] T059 [US3] Register `IUserPanelPreferenceRepository → UserPanelPreferenceRepository` in `src/AskLucy.Persistence/DependencyInjection.cs` (`AddPersistence`, matching every other repository registration) (depends on T058) — also added `DbSet<UserPanelPreference>` to `AskLucyDbContext.cs`
- [X] T060 [US3] EF Core migration `..._AddUserPanelPreference.cs` in `src/AskLucy.Persistence/Migrations/` (depends on T057) — generated via `dotnet ef migrations add`
- [X] T061 [P] [US3] `PanelsContracts.cs` (`SavePanelPreferencesRequest { opacityPercent }`; GET has no request contract and returns `UserPanelPreferenceDto` directly, matching `AiController`'s convention — no separate response-contract type exists elsewhere in this codebase either) in `src/AskLucy.Web/Contracts/PanelsContracts.cs`
- [X] T062 [US3] `PanelsController` (`GET/PUT /api/v1/panels/preferences`, `[Authorize]`) per contracts/panel-preferences-api.md, in `src/AskLucy.Web/Controllers/v1/PanelsController.cs` (depends on T051, T052, T061)
- [X] T062a [US3] Add a `"panels-endpoints"` rate-limit policy (mirroring `"ai-endpoints"`/`"weather-endpoints"`, fixed window, per-user) in `src/AskLucy.Web/Program.cs`, and apply `[EnableRateLimiting("panels-endpoints")]` to `PanelsController` (constitution §6), in `src/AskLucy.Web/Program.cs` + `src/AskLucy.Web/Controllers/v1/PanelsController.cs` (depends on T062)
- [X] T063 [P] [US3] Controller test `PanelsControllerTests.cs` (401 unauthenticated, 200 default, 200 persisted, 200 saved, 400 out-of-range) in `tests/AskLucy.Web.Tests/Panels/` (repo convention: feature-named folder, matching `tests/AskLucy.Web.Tests/Weather/WeatherControllerTests.cs` — not a `Controllers/` folder) — `IUserPanelPreferenceRepository`/`IUnitOfWork` substituted via `ConfigureTestServices`, no live database, matching `WeatherControllerTests`' pattern

### Frontend for User Story 3

- [X] T064 [P] [US3] `panelPreferencesApi.ts` (`getPanelPreferences`/`savePanelPreferences` via the existing `apiFetch` wrapper) in `src/AskLucy.Web/ClientApp/src/features/settings/api/panelPreferencesApi.ts`
- [X] T065 [US3] `panelPreferencesStore.ts` (Zustand + `persist`, `hydrateFromServer()`, optimistic `update(patch)`, `error` field) mirroring `voicePreferencesStore.ts`, in `src/AskLucy.Web/ClientApp/src/viewer/panels/store/panelPreferencesStore.ts` (depends on T064)
- [X] T066 [US3] Replace `FloatingPanel.tsx`'s hardcoded default opacity (T013a) with the live `panelPreferencesStore.opacityPercent` value, in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.tsx` (depends on T065, T035, T013a)
- [X] T067 [US3] Append `Viewer: 8` to `SETTINGS_TAB_INDEX` (appended, not inserted, per research.md Decision 6) in `src/AskLucy.Web/ClientApp/src/features/settings/settingsTabs.ts`
- [X] T068 [US3] Implement `ViewerTab.tsx` (opacity slider bounded `[40, 100]`, reads/writes `panelPreferencesStore`, Alert on `error`) in `src/AskLucy.Web/ClientApp/src/features/settings/pages/ViewerTab.tsx` (depends on T065, T067)
- [X] T069 [US3] Register `ViewerTab` in `src/AskLucy.Web/ClientApp/src/features/settings/pages/SettingsPage.tsx` (depends on T068)
- [X] T070 [P] [US3] Unit test `panelPreferencesStore` optimistic update and revert-on-failure in `src/AskLucy.Web/ClientApp/src/viewer/panels/store/panelPreferencesStore.test.ts`
- [X] T071 [P] [US3] Component test `ViewerTab` slider bounds and save-error surfacing in `src/AskLucy.Web/ClientApp/src/features/settings/pages/ViewerTab.test.tsx`
- [X] T072 [P] [US3] a11y test `ViewerTab.a11y.test.tsx` for the opacity slider
- [X] T073 [P] [US3] Extend `tests/AskLucy.E2E.Tests/AiFloatingPanels.spec.ts` covering the Settings opacity flow (immediate apply on change; persistence verified via the `ask-lucy-panel-preferences` localStorage key surviving a reload, more robust than a computed-CSS-color comparison)
- [X] T074 [US3] Run quickstart.md Scenario 5 manually; not runnable in this environment (same documented caveat as T034/T047) — verified instead via `dotnet build`, `dotnet test` (Panels: 8 Domain + 12 Application + 5 Web, all pass), `npx tsc -b`, `npx eslint`, and `npx vitest run` (115/115 pass) plus manual code review against quickstart.md's exact steps

**Checkpoint**: User Stories 1–3 are all independently functional.

---

## Phase 6: User Story 4 - Panel Reacts to and Informs the Viewer (Priority: P3)

**Goal**: A panel associated with viewer context can trigger a viewer action (highlight/focus), and the
panel visibly reflects when that association becomes stale or invalid.

**Independent Test**: Open a panel carrying a reference to a specific viewer element/location, trigger
an action in the panel, and confirm the viewer responds; then invalidate the association and confirm
the panel visibly indicates it (spec.md US4 Acceptance Scenarios).

### Implementation for User Story 4

- [X] T075 [P] [US4] Implement the `summary` panel type (zod schema, `SummaryPanelRenderer`) in `src/AskLucy.Web/ClientApp/src/viewer/panels/types/summary/SummaryPanel.tsx` — the "locate" context-action button was **not** built into this type-specific renderer (see T077 correction)
- [X] T076 [US4] Register the `summary` type in `src/AskLucy.Web/ClientApp/src/viewer/panels/types/index.ts` (depends on T075, T028)
- [X] T077 [US4] **Design correction during implementation**: `contextAssociation` lives on `FloatingPanel` itself (data-model.md), not inside a type's renderer, whose contract is `{ data: T }` only (contracts/panel-type-registry.md) — widening it per-type would break that documented contract. Implemented instead as a generic `ContextAssociationControls` sub-component in `FloatingPanel.tsx`'s chrome: a "Locate in viewer" button, shown whenever `contextAssociation` has both `layerId` and `elementId`, calling `viewerEngine.select(layerId, elementId)` (FR-014, US4-AS1). Works identically for any current or future panel type, not just `summary`. In `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.tsx` (depends on T075)
- [X] T078 [US4] Subscribe `floatingPanelStore` to `ViewerEventBus` (`layerRemoved` → `invalid`; `contentLoaded` → `stale`, since a viewer object's content refreshing is the clearest "this panel's cached view may be outdated" signal — `selectionChanged` was deliberately not used, since another element being selected elsewhere doesn't invalidate this panel's own association) and set an associated panel's `contextStatus` accordingly (FR-014, US4-AS2; Edge Cases: removed viewer object), in `src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.ts` (depends on T007)
- [X] T079 [US4] Render a visible stale/invalid indicator (`ContextAssociationControls`, distinct icon+tooltip per state) on `FloatingPanel.tsx` when `contextStatus !== 'current'`, in `FloatingPanel.tsx` (depends on T078)
- [X] T080 [P] [US4] Unit test: `floatingPanelStore`'s `ViewerEventBus` subscription sets `contextStatus` correctly on layer removal (`invalid`) and content reload (`stale`), and leaves unassociated panels untouched, in `src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.test.ts`
- [X] T081 [P] [US4] Component test: `FloatingPanel`'s Locate button calls `viewerEngine.select` with the associated layer/element, and the stale/invalid indicators render correctly (moved from `SummaryPanel.test.tsx` per the T077 correction — the behavior under test is generic chrome, not summary-specific), in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.test.tsx`; `summary` type's own registration/schema/rendering covered separately in `SummaryPanel.test.tsx`
- [X] T082 [P] [US4] Extend `tests/AskLucy.E2E.Tests/AiFloatingPanels.spec.ts` covering the panel→viewer Locate action and the viewer→panel invalid-indicator scenario (layer removal)
- [X] T083 [US4] Run quickstart.md Scenario 6 manually; not runnable in this environment (same documented caveat as T034/T047/T074) — verified instead via `npx tsc -b`, `npx eslint`, and `npx vitest run` (126/126 pass) plus manual code review against quickstart.md's exact steps

**Checkpoint**: All four user stories are independently functional. Full feature scope is complete.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Verification and hardening that spans multiple stories.

- [X] T084 [P] Run the full frontend `jest-axe` a11y suite across every new component and fix any violations — all Panels a11y tests pass (9 tests across `FloatingPanel.a11y.test.tsx`/`ViewerTab.a11y.test.tsx`); broader regression check across `features/settings`/`features/viewer`/`viewer` (179 tests) also passes
- [X] T085 [P] Run the full backend `dotnet test` suite and fix any regressions — 1538/1538 pass (Domain 200, Application 935, Infrastructure 116, Web 287), zero failures, zero regressions
- [X] T086 [P] Verify the generated OpenAPI document includes `GET/PUT /api/v1/panels/preferences` with accurate request/response schemas (constitution §6) — verified by starting the app locally and curling `/openapi/v1.json`; both operations present under `/api/v1/panels/preferences`
- [X] T087 [P] Confirm `react-rnd`/`zod` and the built-in panel type modules aren't needlessly duplicated in the production bundle (constitution §15) — ran `npm run build`; both libraries appear in exactly one chunk (bundled with the main chat/viewer surface, appropriate for their small size — not the kind of heavy dependency §15 requires lazy-loading, unlike the Google Maps loader)
- [X] T088 [P] Manually verify keyboard-only operation of drag/resize/minimize/close/focus across all four built-in panel types (constitution §7) — minimize/restore/close/locate are native `<button>`s (already keyboard-operable, a11y-tested); **found and fixed a real gap**: `react-rnd` drag has no built-in keyboard equivalent, so added arrow-key nudge (Shift+arrow for a larger step) on the focused title bar, with tests. Resizing remains pointer-only (react-rnd's own handles aren't focusable) — documented as a known, disclosed limitation in `architecture-notes.md`, not silently shipped
- [X] T089 Decide whether the `PanelHub`/registry pattern warrants an ADR (constitution §17); since both mirror `AgentExecutionHub`/`UserVoicePreference` exactly, documented that conclusion (no new architectural pattern) in `specs/028-ai-floating-panels/architecture-notes.md`
- [X] T090 [P] Add a short architecture note for `viewer/panels/` (registry, store, hub) alongside this spec (constitution §13), in `specs/028-ai-floating-panels/architecture-notes.md`
- [X] T091 Run the complete quickstart.md validation end-to-end (all 7 scenarios); not runnable in this environment (no live backend + authenticated browser session, same documented caveat as every other manual-scenario task in this feature) — verified instead via the cumulative automated suite (backend 1538 tests, frontend 249 tests, `tsc -b`, `eslint`, production build, OpenAPI curl check) plus manual code review against every scenario's exact steps; the live-verification gap is explicitly recorded in `architecture-notes.md`, not silently assumed passing
- [X] T092 Final self-review against constitution §16 Quality Gates before requesting code review — (1) Architecture compliance: no Dependency Rule violations, Domain/Application/Infrastructure/Persistence/Web layering followed throughout; (2) Automated testing: all new/changed behavior has passing tests; (3) Documentation: spec/plan/research/data-model/contracts/quickstart/tasks kept current with corrections noted as found, architecture-notes.md added; (4) Accessibility: reviewed, one real gap found and partially fixed (drag) and disclosed (resize), not swept under the rug; (5) Performance: no dedicated automated perf test (consistent with spec 027 precedent), but `MAX_CONCURRENT_PANELS` bounds worst-case cost by design and bundle impact was checked; (6) Security: `PanelHub`/`PanelsController` both `[Authorize]`+rate-limited, per-user group delivery verified by test, confirmed zero uses of `dangerouslySetInnerHTML`/`eval`/`new Function` anywhere in `viewer/panels/`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3–6)**: All depend on Foundational completion.
  - US1 (P1) needs only the Foundational shell plus its own hub/registration work — no dependency on
    US2–US4.
  - US2 (P2) modifies `FloatingPanel.tsx` (built minimally in Foundational, extended here with
    `react-rnd`) — functionally independent of US1's transport/types work, but touches the same file,
    so in practice implement US1 → US2 in order.
  - US3 (P3) is almost entirely independent (new backend aggregate + new Settings tab); its only
    frontend touchpoint on shared code is T066 (`FloatingPanel.tsx` opacity styling), so it can be
    built in parallel with US2 by a second developer once Foundational lands.
  - US4 (P3) depends on `floatingPanelStore` (Foundational) and the `viewer/` engine's
    `ViewerEventBus`/`ViewerEngine` (spec 027, pre-existing) — independent of US2/US3's file changes
    except for shared `FloatingPanel.tsx` edits (T079), so implement after US2 to avoid merge churn.
- **Polish (Phase 7)**: Depends on all desired user stories being complete.

### Parallel Opportunities

- All Setup tasks marked [P] can run together.
- Within Foundational, T004 (types) can run before T005 (registry); T006/T010/T011/T012 (tests) can run
  in parallel with each other once their subject exists; T016/T017 (component/a11y tests) run in
  parallel once T013 exists.
- Once Foundational completes, **US3's backend track (T048–T063)** can proceed almost entirely in
  parallel with the frontend interaction track (US2), since it touches none of the same files — a
  natural two-developer split, joining only at T066 (`FloatingPanel.tsx` opacity styling, after US2's
  `react-rnd` wiring lands).
- Every task marked [P] within a phase targets a different file from every other [P] task in that same
  batch.

---

## Parallel Example: Foundational Phase

```bash
# Test tasks once their subject file exists (no shared files with each other):
Task: "Unit test PanelTypeRegistry register/resolve/duplicate-key behavior in registry.test.ts"
Task: "Unit test openPanel validation states in floatingPanelStore.test.ts"
Task: "Unit test cascade placement offsets and wraparound in floatingPanelStore.test.ts"
Task: "Unit test LRU eviction at MAX_CONCURRENT_PANELS in floatingPanelStore.test.ts"
```

## Parallel Example: User Story 3 (backend + frontend split)

```bash
# Backend developer:
Task: "UserPanelPreference entity in src/AskLucy.Domain/Panels/UserPanelPreference.cs"
Task: "IUserPanelPreferenceRepository interface in src/AskLucy.Application/Panels/Abstractions/"
Task: "UserPanelPreferenceDto in src/AskLucy.Application/Panels/UserPanelPreferenceDto.cs"

# Frontend developer (once T064 panelPreferencesApi.ts exists):
Task: "panelPreferencesStore.ts in viewer/panels/store/panelPreferencesStore.ts"
Task: "Append Viewer tab to settingsTabs.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks everything else).
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: run quickstart.md Scenario 1 and Scenario 2; confirm a panel appears over the
   viewer for a valid request and a clear fallback appears for an unknown-type/malformed one.
5. Deploy/demo if ready — this is a legitimate, if modest, shippable increment (a working, if
   not-yet-draggable, AI-to-panel path).

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. Add US1 → validate independently → deploy/demo (MVP).
3. Add US2 → validate independently (full drag/resize/minimize/close/focus) → deploy/demo.
4. Add US3 → validate independently (opacity preference) → deploy/demo.
5. Add US4 → validate independently (viewer context communication) → deploy/demo. **Full feature scope
   complete.**
6. Polish phase → final hardening pass.

### Parallel Team Strategy

With two developers: both complete Setup + Foundational together; then one developer takes the
frontend interaction track (US1 → US2 → US4 in order, since each touches `FloatingPanel.tsx`) while the
other takes the backend-heavy US3 opacity-preference track (near-fully parallel once Foundational
exists), rejoining for the Polish phase.

---

## Notes

- [P] tasks touch different files with no dependency on an incomplete task.
- [Story] labels map every implementation/test task to its spec.md user story for traceability.
- US1/US2/US4 share the single `FloatingPanel.tsx`/`floatingPanelStore.ts` files across several tasks —
  those tasks are intentionally **not** marked [P] against each other even within the same phase.
- `types/index.ts` (built-in panel type registration) is touched once per story (T028 in US1, T039 in
  US2, T076 in US4) — never marked [P] against another edit to the same file.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
- Every test task pairs with the implementation task it verifies, per constitution §10/§18 — do not
  defer them to a follow-up.
