---

description: "Task list for Immersive Viewer Platform for AI-Assisted Urban Design (SPEC-027)"
---

# Tasks: Immersive Viewer Platform for AI-Assisted Urban Design

**Input**: Design documents from `/specs/027-immersive-viewer-platform/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included throughout — not optional here. Constitution §10/§18/§19 requires tests for new/changed
behavior in the same PR that introduces it; this is a governing project rule, not a per-feature choice.

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P1/P1/P2/P2/P3) to enable
independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US6)
- Paths are relative to the repository root (`D:\Workshop\BIM Catalyst\Web Apps\Platform\Ask Lucy`)

## Path Conventions

Existing Clean Architecture + React SPA layout (see plan.md "Project Structure") — no new project:
- Backend: `src/AskLucy.Domain/`, `src/AskLucy.Application/`, `src/AskLucy.Infrastructure/`, `src/AskLucy.Web/`
- Frontend: `src/AskLucy.Web/ClientApp/src/`
- Tests: `tests/AskLucy.*.Tests/`, plus co-located `*.test.tsx`/`*.a11y.test.tsx` in `ClientApp/src`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the one new frontend dependency this feature needs and scaffold empty package
directories so subsequent tasks have somewhere to land.

- [X] T001 Add `@googlemaps/js-api-loader` and `@types/google.maps` to `src/AskLucy.Web/ClientApp/package.json` and run `npm install`
- [X] T002 [P] Add `VITE_GOOGLE_MAPS_API_KEY` to `src/AskLucy.Web/ClientApp/.env.example` and its Vite env type declaration in `src/AskLucy.Web/ClientApp/src/vite-env.d.ts`
- [X] T003 [P] Scaffold empty `engine/`, `camera/`, `layers/gis/`, `layers/model/`, `selection/`, `overlays/`, `api/`, `store/` subfolders under `src/AskLucy.Web/ClientApp/src/viewer/`
- [X] T004 [P] Scaffold empty `api/`, `hooks/`, `components/` subfolders under `src/AskLucy.Web/ClientApp/src/features/viewer/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared viewer-engine shell (types, store, facade, event bus, placeholder/fallback
rendering, and its mount point in the page) that every user story builds on. No story-specific
commands (map, camera, selection, overlays) are implemented yet — only the plumbing they'll attach to.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 [P] Define `RenderLayer`, `RenderLayerInput`, `OverlayInput` types per data-model.md in `src/AskLucy.Web/ClientApp/src/viewer/api/layers.ts`
- [X] T006 [P] Define `ViewerCommand`/`ViewerCommandResult` discriminated-union types per contracts/viewer-engine-api.md in `src/AskLucy.Web/ClientApp/src/viewer/api/commands.ts`
- [X] T007 [P] Define `ViewerEvent` discriminated-union types per contracts/viewer-engine-api.md in `src/AskLucy.Web/ClientApp/src/viewer/api/events.ts`
- [X] T008 Implement the `on`/`off`/`emit` pub-sub in `src/AskLucy.Web/ClientApp/src/viewer/engine/viewerEventBus.ts` (depends on T007)
- [X] T009 Implement an independent WebGL2-support probe in `src/AskLucy.Web/ClientApp/src/hooks/useWebGLSupport.ts` (a small, self-contained `canvas.getContext('webgl2')` check — duplicating the few lines already used by `useSceneQualityTier.ts` is acceptable per constitution §2.III's non-business-logic carve-out) **without modifying `useSceneQualityTier.ts` or anything `AiPresenceCard` depends on**, per FR-004's "unaffected by the new viewer" requirement
- [X] T010 Implement `viewerEngineStore.ts` (Zustand, session-scoped, no `persist` — matches `workspaceOverlayStore` convention) holding `contentMode`/`camera`/`selection`/`layers` per data-model.md, in `src/AskLucy.Web/ClientApp/src/viewer/store/viewerEngineStore.ts` (depends on T005, T006, T007)
- [X] T011 Implement the `ViewerEngine` facade class (constructor wiring `viewerEngineStore` + `viewerEventBus`, a generic command-validation helper for unknown-target failures) implementing the shell of `IViewerEngine` in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.ts` (depends on T008, T010)
- [X] T012 [P] Implement `PlaceholderRenderTarget.tsx` — a simple static, `aria-hidden`, non-Three.js branded background (no sphere) in `src/AskLucy.Web/ClientApp/src/viewer/engine/PlaceholderRenderTarget.tsx`
- [X] T013 [P] Implement `ViewerFallback.tsx` — the non-interactive fallback presentation for browsers without WebGL support (FR-005) in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerFallback.tsx` (depends on T009)
- [X] T014 Implement `ViewerSurface.tsx` mounting logic that switches between `ViewerFallback` and `PlaceholderRenderTarget` based on WebGL capability and `viewerEngineStore.contentMode` in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx` (depends on T010, T011, T012, T013)
- [X] T015 Replace `WorkspaceSurface`'s gradient mount with `ViewerSurface` in `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, keeping `AiPresenceCard` mounted exactly as before (FR-004) (depends on T014)
- [X] T016 [P] Unit test `viewerEngineStore` initial state and transitions in `src/AskLucy.Web/ClientApp/src/viewer/store/viewerEngineStore.test.ts`
- [X] T017 [P] a11y test for `ViewerFallback` (aria-hidden, no interactive/focusable elements) in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerFallback.a11y.test.tsx`
- [X] T018 [P] Component test: `ViewerSurface` renders the placeholder by default and the fallback when WebGL is unavailable, in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.test.tsx`
- [X] T019 [P] Regression test verifying `AiPresenceCard` still renders and behaves unchanged after the `ChatPage` integration, extending `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.test.tsx`

**Checkpoint**: Foundation ready — the viewer is on the page, shows its placeholder or fallback
correctly, and `AiPresenceCard` is verifiably untouched. User story implementation can now begin.

---

## Phase 3: User Story 1 - Arrive in an immersive, extensible viewer workspace (Priority: P1) 🎯 MVP

**Goal**: The viewer occupies the majority of the viewport as the primary workspace surface, shows its
placeholder immediately without blocking, and the pre-existing decorative-sphere presence card is
demonstrably unaffected.

**Independent Test**: Load the main workspace; confirm the viewer occupies the majority of the
viewport, renders without errors, shows a non-blocking placeholder, and `AiPresenceCard` continues to
work exactly as before (spec.md US1 Acceptance Scenarios).

### Implementation for User Story 1

- [X] T020 [P] [US1] Tune `ViewerSurface` layout (sx/CSS) to occupy at least 70% of the viewport across mobile/tablet/desktop breakpoints (SC-001) in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx`
- [X] T021 [US1] Verify/adjust z-index and `pointerEvents` in `ChatPage.tsx` so `WorkspaceOverlay`, its toolbar, `AiPresenceCard`, and `ChatAssistantWidget` all remain fully interactive above `ViewerSurface`
- [X] T022 [P] [US1] a11y test confirming `ViewerSurface`'s placeholder state is `aria-hidden` and never traps keyboard focus, in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.a11y.test.tsx`
- [X] T023 [P] [US1] Playwright E2E smoke spec asserting the viewer covers the majority of the viewport and `AiPresenceCard` still renders, in `tests/AskLucy.E2E.Tests/ImmersiveViewerPlatform.spec.ts`
- [X] T024 [US1] Run quickstart.md Scenario 1 (steps 1–2) manually; fix any issues found

**Checkpoint**: User Story 1 is fully functional and independently testable/demoable — this is the
suggested MVP stopping point.

---

## Phase 4: User Story 2 - See my current location represented in the viewer (Priority: P1)

**Goal**: Once the user's location resolves, the viewer replaces its placeholder with a Google Maps
`WebGLOverlayView` GIS layer centered on that location; denial/unavailability degrades gracefully with
no error shown; later unavailability reverts to the placeholder.

**Independent Test**: Grant location permission, load the workspace, confirm the placeholder→map
transition within ~5s; separately, deny permission and confirm the viewer stays on its placeholder
with no error (spec.md US2 Acceptance Scenarios).

### Implementation for User Story 2

- [X] T025 [P] [US2] Implement `useGeolocation.ts` (permission request via `navigator.geolocation.getCurrentPosition`, `enableHighAccuracy: false` per research.md Decision 8, granted/denied/unsupported/timeout states) in `src/AskLucy.Web/ClientApp/src/features/viewer/hooks/useGeolocation.ts`
- [X] T026 [P] [US2] Unit test `useGeolocation` covering granted/denied/unsupported/timeout via a mocked `navigator.geolocation`, in `src/AskLucy.Web/ClientApp/src/features/viewer/hooks/useGeolocation.test.ts`
- [X] T027 [US2] Implement the `addLayer`/`removeLayer` commands for real (layer registry CRUD, duplicate-id failure) in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.ts` (depends on T011)
- [X] T028 [P] [US2] Unit test `addLayer`/`removeLayer` success and failure cases in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.test.ts`
- [X] T029 [US2] Implement the `zoomToLocation` command (coordinate-range validation, failure on out-of-range) in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.ts` (depends on T027)
- [X] T030 [P] [US2] Unit test `zoomToLocation` success and failure in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.test.ts`
- [X] T031 [US2] Implement `GoogleMapsGisLayer` — lazily-loaded Google Maps JS bootstrap + a `WebGLOverlayView` bridging a `THREE.Scene`/`PerspectiveCamera`/`WebGLRenderer` to the map's own GL context (research.md Decision 3) in `src/AskLucy.Web/ClientApp/src/viewer/layers/gis/GoogleMapsGisLayer.ts`
- [X] T032 [US2] Implement `MapRenderTarget.tsx` (mounts the Google Maps `<div>` + `GoogleMapsGisLayer`, emits `contentLoaded`) in `src/AskLucy.Web/ClientApp/src/viewer/engine/MapRenderTarget.tsx` (depends on T031)
- [X] T032a [US2] Implement a lightweight device-capability check for `MapRenderTarget`/`GoogleMapsGisLayer` (reusing `useWebGLSupport` + the existing mobile-breakpoint pattern) that reduces overlay rendering complexity and/or pauses auto-rotation on detected low-end/mobile devices (FR-005a/SC-004a), in `src/AskLucy.Web/ClientApp/src/viewer/layers/gis/GoogleMapsGisLayer.ts` (depends on T031)
- [X] T032b [P] [US2] Unit test verifying the degradation check reduces complexity/pauses rotation under a simulated low-end profile, in `src/AskLucy.Web/ClientApp/src/viewer/layers/gis/GoogleMapsGisLayer.test.ts`
- [X] T033 [US2] Wire `ViewerSurface.tsx` to switch to `MapRenderTarget` and call `addLayer` + `zoomToLocation` once `useGeolocation` resolves (depends on T025, T029, T032)
- [X] T034 [US2] Implement the graceful-hidden-fallback path: on denied/unsupported/timeout, `contentMode` stays `'placeholder'` and no map/layer command is ever issued (FR-008), extending `ViewerSurface.tsx` (depends on T033)
- [X] T035 [US2] Implement FR-012's revert-to-placeholder behavior when location becomes unavailable after the map is already active (e.g. permission revoked mid-session), in `useGeolocation.ts`/`ViewerSurface.tsx` (depends on T034)
- [X] T036 [P] [US2] Component test: `ViewerSurface` transitions placeholder→map on geolocation resolve and reverts on later unavailability (mocking `GoogleMapsGisLayer`/`MapRenderTarget`), in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.test.tsx`
- [X] T037 [P] [US2] Component test: `ViewerSurface` stays on the placeholder and issues no map/layer commands when geolocation is denied
- [X] T038 [P] [US2] Confirm the Google Maps loader and `GoogleMapsGisLayer` are code-split via dynamic `import()` inside `MapRenderTarget.tsx` so they never load until map mode activates (constitution §15)
- [X] T039 [P] [US2] Extend `tests/AskLucy.E2E.Tests/ImmersiveViewerPlatform.spec.ts` with a granted-location scenario (`context.setGeolocation`) asserting the map renders
- [X] T040 [P] [US2] Extend the same E2E spec with a denied-location scenario asserting the placeholder persists with no thrown errors
- [X] T041 [US2] Run quickstart.md Scenario 1 (steps 3–5) and Scenario 2 manually; fix any issues found

**Checkpoint**: User Stories 1 and 2 both work independently — the viewer now genuinely grounds itself
in the user's location.

---

## Phase 5: User Story 3 - Control camera perspective and motion (Priority: P1)

**Goal**: Toolbar controls toggle the camera between isometric and plan view, and start/stop automatic
rotation, independently of each other and of whichever content mode is active.

**Independent Test**: With any content showing, use the toolbar to switch view mode and to toggle
rotation; confirm each control produces an immediate, visible, reversible change (spec.md US3
Acceptance Scenarios).

### Implementation for User Story 3

- [X] T042 [US3] Implement the `setViewMode` command (isometric/plan), applying camera-perspective logic to whichever real render target is active (no-op when the placeholder is active, per FR-013 as revised), in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.ts` + `src/AskLucy.Web/ClientApp/src/viewer/camera/cameraViewMode.ts` (depends on T011, T033)
- [X] T043 [P] [US3] Unit test `setViewMode` success + `viewModeChanged` event emission, in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.test.ts`
- [X] T044 [US3] Implement the `setRotationEnabled` command and a rotation driver applying/holding orientation on whichever real render target is active (no-op when the placeholder is active, per FR-017 as revised), in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.ts` + `src/AskLucy.Web/ClientApp/src/viewer/camera/rotationDriver.ts` (depends on T042)
- [X] T045 [P] [US3] Unit test `setRotationEnabled` success, `rotationChanged` event emission, and smooth (not jump-cut) resume, in `src/AskLucy.Web/ClientApp/src/viewer/camera/rotationDriver.test.ts`
- [X] T046 [US3] Default `rotationEnabled` to `false` at store initialization when `usePrefersReducedMotion()` is true (FR-016), in `src/AskLucy.Web/ClientApp/src/viewer/store/viewerEngineStore.ts`
- [X] T046a [P] [US3] Unit test verifying `viewerEngineStore` initializes `camera.rotationEnabled` to `false` when `usePrefersReducedMotion()` is true, and to `true` otherwise (FR-016/SC-008), extending `src/AskLucy.Web/ClientApp/src/viewer/store/viewerEngineStore.test.ts` (depends on T046)
- [X] T047 [US3] Rename `workspaceOverlayStore.viewMode: '2D'|'3D'` to `'isometric'|'plan'` and repoint `useViewModeControl()` to call `viewerEngine.setViewMode` instead of its current cosmetic gradient toggle (research.md Decision 4), in `src/AskLucy.Web/ClientApp/src/store/workspaceOverlayStore.ts` and `src/AskLucy.Web/ClientApp/src/features/chat/components/workspaceControls.tsx`
- [X] T048 [US3] Remove the now-dead viewMode-driven gradient-angle logic left over from the old `WorkspaceSurface`, cleaning up `src/AskLucy.Web/ClientApp/src/features/chat/components/WorkspaceSurface.tsx` (delete the file if nothing else references it)
- [X] T049 [P] [US3] Implement `RotationToggleButton.tsx`, styled like `ThemeToggleButton.tsx` and reusing `CIRCULAR_ACTION_CHROME` (research.md Decision 5), in `src/AskLucy.Web/ClientApp/src/features/viewer/components/RotationToggleButton.tsx`
- [X] T050 [US3] Mount `RotationToggleButton` in `WorkspaceOverlay`'s `topClusterLeading` slot alongside `ThemeToggleButton`, in `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx` (depends on T049)
- [X] T051 [P] [US3] a11y test for `RotationToggleButton` (keyboard operable, `aria-pressed`/label reflects state) in `src/AskLucy.Web/ClientApp/src/features/viewer/components/RotationToggleButton.a11y.test.tsx`
- [X] T052 [P] [US3] Component test: the repurposed view-mode control toggles isometric/plan and calls `viewerEngine.setViewMode`, in `src/AskLucy.Web/ClientApp/src/features/chat/components/workspaceControls.test.tsx`
- [X] T053 [P] [US3] Extend `tests/AskLucy.E2E.Tests/ImmersiveViewerPlatform.spec.ts` asserting both toggle buttons visibly change camera/rotation state
- [X] T054 [US3] Run quickstart.md Scenario 3 manually; fix any issues found

**Checkpoint**: User Stories 1–3 (all P1) are complete — the full P1 slice of this feature is done and
independently demoable.

---

## Phase 6: User Story 4 - See local weather at a glance (Priority: P2)

**Goal**: A compact widget shows the resolved location's name, temperature, and a weather-condition
icon, refreshing periodically, degrading gracefully (stale-or-hidden, never broken) on failure.

**Independent Test**: With location granted, confirm the widget shows name/temperature/icon; deny
location and confirm it doesn't appear; simulate the weather endpoint failing and confirm graceful
degradation (spec.md US4 Acceptance Scenarios).

### Backend

- [X] T055 [P] [US4] `WeatherCondition` enum (`Clear`/`PartlyCloudy`/`Cloudy`/`Fog`/`Rain`/`Snow`/`Thunderstorm`/`Windy`) in `src/AskLucy.Domain/Weather/WeatherCondition.cs`
- [X] T056 [P] [US4] `WeatherProviderUnavailableException` in `src/AskLucy.Domain/Weather/WeatherProviderUnavailableException.cs`
- [X] T057 [P] [US4] `WeatherSnapshotDto` per data-model.md in `src/AskLucy.Application/Weather/WeatherSnapshotDto.cs`
- [X] T058 [P] [US4] `IWeatherProvider` interface in `src/AskLucy.Application/Abstractions/IWeatherProvider.cs`
- [X] T059 [US4] `GetCurrentWeatherQuery` + `GetCurrentWeatherQueryValidator` (latitude/longitude range) in `src/AskLucy.Application/Weather/Queries/GetCurrentWeather/` (depends on T057, T058)
- [X] T060 [US4] `GetCurrentWeatherQueryHandler` calling `IWeatherProvider` in the same folder (depends on T059)
- [X] T061 [P] [US4] Unit test `GetCurrentWeatherQueryHandlerTests.cs` (mocked `IWeatherProvider`, success + provider-unavailable) in `tests/AskLucy.Application.Tests/Weather/`
- [X] T062 [P] [US4] Unit test `GetCurrentWeatherQueryValidatorTests.cs` (latitude/longitude range) in `tests/AskLucy.Application.Tests/Weather/`
- [X] T063 [P] [US4] `WeatherOptions.cs` (`BaseUrl`, no key — keyless provider per research.md Decision 6) in `src/AskLucy.Infrastructure/Weather/WeatherOptions.cs`
- [X] T064 [US4] `WeatherProvider.cs` implementing `IWeatherProvider` (HTTP call, upstream-code → `WeatherCondition` mapping per research.md Decision 7) in `src/AskLucy.Infrastructure/Weather/WeatherProvider.cs` (depends on T058, T063)
- [X] T065 [P] [US4] Unit test `WeatherProviderTests.cs` (`StubHttpMessageHandler`, success/timeout/5xx→`WeatherProviderUnavailableException`, condition-mapping table) in `tests/AskLucy.Infrastructure.Tests/Weather/`
- [X] T066 [US4] Register `AddHttpClient("Weather", ...)`, `AddOptions<WeatherOptions>`, and `IWeatherProvider → WeatherProvider` in `src/AskLucy.Infrastructure/DependencyInjection.cs` (depends on T064)
- [X] T067 [US4] `WeatherController.cs` (`GET /api/v1/weather/current`) in `src/AskLucy.Web/Controllers/v1/WeatherController.cs` (depends on T060)
- [X] T068 [US4] Add the `"weather-endpoints"` rate-limit policy (fixed window, 30/min/user) in `src/AskLucy.Web/Program.cs` (depends on T067)
- [X] T069 [US4] Add a `WeatherProviderUnavailableException` → 502 Problem Details arm in `src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs` (depends on T056)
- [X] T070 [P] [US4] Controller test `WeatherControllerTests.cs` (200 success, 400 invalid coordinates, 502 provider-down) in `tests/AskLucy.Web.Tests/Controllers/`

### Frontend

- [X] T071 [P] [US4] `weatherApi.ts` (`getCurrentWeather(lat, lon)` via the existing `apiFetch` wrapper) in `src/AskLucy.Web/ClientApp/src/features/viewer/api/weatherApi.ts`
- [X] T072 [US4] `useCurrentWeather.ts` (TanStack Query, periodic refetch interval, staleness derived from `observedAtUtc`) in `src/AskLucy.Web/ClientApp/src/features/viewer/hooks/useCurrentWeather.ts` (depends on T071)
- [X] T073 [US4] `LocationWeatherWidget.tsx` (location name, temperature, condition icon via Remix Icon mapping, staleness indicator, hides on denied-location/persistent-error per FR-011/US4-AC3/AC4) in `src/AskLucy.Web/ClientApp/src/features/viewer/components/LocationWeatherWidget.tsx` (depends on T072, T025)
- [X] T074 [US4] Mount `LocationWeatherWidget` over `ViewerSurface` in `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx` (depends on T073)
- [X] T075 [P] [US4] a11y test `LocationWeatherWidget.a11y.test.tsx`
- [X] T076 [P] [US4] Component test `LocationWeatherWidget.test.tsx` (msw-mocked success/error/stale scenarios) in `src/AskLucy.Web/ClientApp/src/features/viewer/components/`
- [X] T076a [P] [US4] Component test verifying `useCurrentWeather`/`LocationWeatherWidget` stops fetching and the widget disappears once `useGeolocation` transitions from resolved to unavailable mid-session (FR-012's weather-widget clause), extending `src/AskLucy.Web/ClientApp/src/features/viewer/components/LocationWeatherWidget.test.tsx` (depends on T035, T073)
- [X] T077 [P] [US4] Extend `tests/AskLucy.E2E.Tests/ImmersiveViewerPlatform.spec.ts` asserting the widget appears with granted location and is absent when denied
- [X] T078 [US4] Run quickstart.md Scenario 4 and Scenario 6 (curl) manually; fix any issues found

**Checkpoint**: User Story 4 is independently functional — the weather widget works whether or not
selection (US5) or the full command API (US6) exist yet.

---

## Phase 7: User Story 5 - Select and highlight content in the viewer (Priority: P2)

**Goal**: An addressable element in the viewer (the current-location marker) can be selected,
visually highlighted, and deselected, with deterministic resolution when content overlaps.

**Independent Test**: Select the current-location marker, confirm it's visually distinguished from
unselected content; clear the selection or select something else and confirm the highlight moves/clears
(spec.md US5 Acceptance Scenarios).

### Implementation for User Story 5

- [X] T079 [US5] Implement the `select`/`clearSelection` commands (layer/element lookup, failure on unknown ids) in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.ts` (depends on T027)
- [X] T080 [P] [US5] Unit test `select`/`clearSelection` success, failure, and `selectionChanged` event emission, in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.test.ts`
- [X] T081 [US5] Implement the deterministic overlap-resolution rule (topmost/foreground wins) in `src/AskLucy.Web/ClientApp/src/viewer/selection/resolveSelection.ts`
- [X] T082 [P] [US5] Unit test `resolveSelection` overlap cases in `src/AskLucy.Web/ClientApp/src/viewer/selection/resolveSelection.test.ts`
- [X] T083 [US5] Apply a visually distinguishable highlight style to the selected element in `GoogleMapsGisLayer.ts`'s current-location marker (depends on T031, T079)
- [X] T084 [US5] Register the current-location marker as a selectable element once the map layer finishes loading, in `src/AskLucy.Web/ClientApp/src/viewer/layers/gis/GoogleMapsGisLayer.ts`/`ViewerSurface.tsx` (depends on T033, T079)
- [X] T085 [P] [US5] Component test: selecting/clearing the current-location marker updates `viewerEngineStore.selection` and its highlight style
- [X] T086 [P] [US5] Extend `tests/AskLucy.E2E.Tests/ImmersiveViewerPlatform.spec.ts` selecting and deselecting the location marker
- [X] T087 [US5] Run the manual selection check from quickstart.md (devtools `viewerEngine.select(...)`/`clearSelection()`)

**Checkpoint**: User Stories 1–5 are all independently functional.

---

## Phase 8: User Story 6 - Expose viewer capabilities for future AI-driven control (Priority: P3)

**Goal**: The remaining commands (`displayContent`, `createOverlay`) are implemented and the full
command/event contract is proven end-to-end without any AI agent involved.

**Independent Test**: Invoke every documented command directly (devtools console or a test harness)
and confirm each produces the documented outcome and event, with zero AI-agent code involved (spec.md
US6 Acceptance Scenarios, SC-006).

### Implementation for User Story 6

- [X] T088 [US6] Implement the `displayContent` command (validates the target layer exists and supports the given content shape; fails clearly otherwise) in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.ts`
- [X] T089 [P] [US6] Unit test `displayContent` success and unsupported-content-type failure, in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.test.ts`
- [X] T090 [US6] Implement the `createOverlay` command and the `Overlay` contract in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.ts` + `src/AskLucy.Web/ClientApp/src/viewer/overlays/Overlay.ts`
- [X] T091 [P] [US6] Unit test `createOverlay` success, failure, and event emission, in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.test.ts`
- [X] T092 [US6] Write a comprehensive facade test exercising every command in contracts/viewer-engine-api.md end-to-end (all commands, all documented failure examples, every corresponding event) in `src/AskLucy.Web/ClientApp/src/viewer/engine/ViewerEngine.contract.test.ts`
- [X] T093 [US6] Expose the mounted `viewerEngine` instance as `window.__askLucyViewerEngine` in development builds only (`import.meta.env.DEV` guard) for devtools/manual verification, in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx`
- [X] T094 [P] [US6] Write the command/event API reference (mirrors contracts/viewer-engine-api.md, for future AI-agent implementers per constitution §13) in `src/AskLucy.Web/ClientApp/src/viewer/README.md`
- [X] T095 [P] [US6] Extend `tests/AskLucy.E2E.Tests/ImmersiveViewerPlatform.spec.ts` running quickstart.md Scenario 5's exact command sequence via `page.evaluate`, asserting every `ok`/`error` outcome and its event
- [X] T096 [US6] Run quickstart.md Scenario 5 manually in the browser devtools console

**Checkpoint**: All six user stories are independently functional. Full feature scope is complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Verification and hardening that spans multiple stories.

- [X] T097 [P] Run the full frontend `jest-axe` a11y suite across every new component and fix any violations
- [X] T098 [P] Run the full backend `dotnet test` suite and fix any regressions
- [X] T099 [P] Verify the generated OpenAPI document includes `GET /api/v1/weather/current` with an accurate request/response schema (constitution §6)
- [X] T100 [P] Inspect the production build output to confirm the Google Maps loader and `GoogleMapsGisLayer` are excluded from the initial route bundle (constitution §15)
- [X] T101 [P] Manually profile the map/GIS content mode for ~60fps on a throttled/mid-tier device (FR-005a/SC-004a)
- [X] T102 Write an ADR for the `ViewerRenderTarget` adapter pattern (research.md Decision 3) if it's judged a new cross-cutting architectural pattern (constitution §17), in `docs/adr/`
- [X] T103 [P] Add a short architecture note for the new `viewer/` package (layer separation, command/event contract) alongside this spec (constitution §13), in `specs/027-immersive-viewer-platform/`
- [X] T104 Run the complete quickstart.md validation end-to-end (all 6 scenarios) and fix any discrepancies found
- [X] T105 Final self-review against constitution §16 Quality Gates before requesting code review

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3–8)**: All depend on Foundational completion.
  - US1, US2, US3 (all P1) have a natural build-order dependency: US2 needs `addLayer`/`removeLayer`
    (T027, introduced in US2 itself) and the render-target switch (T033); US3's camera commands apply
    to whichever render target US2 made real. In practice, implement US1 → US2 → US3 in order even
    though each has its own independently-testable checkpoint.
  - US4 (weather) is functionally independent of US2/US3/US5/US6 except for reusing US2's
    `useGeolocation` hook (T025) — it can be built in parallel by a second developer once T025 lands.
  - US5 (selection) depends on US2's `GoogleMapsGisLayer`/marker existing (T031, T033) to have
    something to select.
  - US6 (full command contract) depends on US2/US5 having implemented `addLayer`/`select` etc.; it
    adds only the two remaining commands (`displayContent`, `createOverlay`) and the end-to-end proof.
- **Polish (Phase 9)**: Depends on all desired user stories being complete.

### Parallel Opportunities

- All Setup tasks marked [P] can run together.
- Within Foundational, T005–T007 (type files) can run together; T012/T013 (placeholder/fallback
  components) can run together once T009 lands.
- Once Foundational completes, **US4's backend track (T055–T070)** can proceed almost entirely in
  parallel with the frontend viewer-engine track (US2/US3/US5/US6), since it touches none of the same
  files — a natural two-developer split.
- Every task marked [P] within a phase targets a different file from every other [P] task in that same
  batch.

---

## Parallel Example: Foundational Phase

```bash
# Type definitions (no shared files, no dependencies on each other):
Task: "Define RenderLayer/RenderLayerInput/OverlayInput types in viewer/api/layers.ts"
Task: "Define ViewerCommand/ViewerCommandResult types in viewer/api/commands.ts"
Task: "Define ViewerEvent types in viewer/api/events.ts"
```

## Parallel Example: User Story 4 (backend + frontend split)

```bash
# Backend developer:
Task: "WeatherCondition enum in src/AskLucy.Domain/Weather/WeatherCondition.cs"
Task: "WeatherSnapshotDto in src/AskLucy.Application/Weather/WeatherSnapshotDto.cs"
Task: "IWeatherProvider interface in src/AskLucy.Application/Abstractions/IWeatherProvider.cs"
Task: "WeatherOptions.cs in src/AskLucy.Infrastructure/Weather/WeatherOptions.cs"

# Frontend developer (once T025 useGeolocation exists from US2):
Task: "weatherApi.ts in features/viewer/api/weatherApi.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks everything else).
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: run quickstart.md Scenario 1 (steps 1–2); confirm the viewer occupies the
   majority of the viewport, shows its placeholder immediately, and `AiPresenceCard` is unaffected.
5. Deploy/demo if ready — this is a legitimate, if modest, shippable increment.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. Add US1 → validate independently → deploy/demo (MVP).
3. Add US2 → validate independently (location-grounded map) → deploy/demo.
4. Add US3 → validate independently (camera controls) → deploy/demo. **All P1 scope now complete.**
5. Add US4 → validate independently (weather widget) → deploy/demo.
6. Add US5 → validate independently (selection/highlight) → deploy/demo.
7. Add US6 → validate independently (full command/event contract) → deploy/demo. **Full feature scope complete.**
8. Polish phase → final hardening pass.

### Parallel Team Strategy

With two developers: both complete Setup + Foundational together; then one developer takes the
frontend viewer-engine track (US1 → US2 → US3 → US5 → US6 in order, since each depends on the
previous one's engine additions) while the other takes the backend-heavy US4 weather track
(near-fully parallel once T025 exists), rejoining for the Polish phase.

---

## Notes

- [P] tasks touch different files with no dependency on an incomplete task.
- [Story] labels map every implementation/test task to its spec.md user story for traceability.
- US1/US2/US3 share the single `ViewerEngine.ts`/`viewerEngineStore.ts` files across several tasks —
  those tasks are intentionally **not** marked [P] against each other even within the same phase.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
- Every test task pairs with the implementation task it verifies, per constitution §10/§18 — do not
  defer them to a follow-up.
