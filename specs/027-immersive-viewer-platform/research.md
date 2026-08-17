# Research: Immersive Viewer Platform for AI-Assisted Urban Design

Phase 0 output for [plan.md](./plan.md). Each decision below resolves a technical unknown surfaced
while reading the existing codebase (`src/AskLucy.Web/ClientApp/src/features/chat/scene/*`,
`src/AskLucy.Web/ClientApp/src/components/workspace-shell/*`, `src/AskLucy.Infrastructure/*`) against
the approved spec.

## Decision 1: The viewer engine builds on `@react-three/fiber`, not raw imperative Three.js

**Decision**: The new `viewer/engine` module renders through `@react-three/fiber` (R3F) + `@react-three/drei`
for its own placeholder/model/overlay content, the same stack `SceneBackground.tsx`/`ReactiveSphere.tsx`
already use.

**Rationale**: R3F is already a first-class dependency (`^9.6.1`) with an established idiom in this
codebase (declarative scene graph, `useFrame`, `<Canvas>` lifecycle, `PerformanceMonitor`-driven quality
tiers). Introducing a second, imperative Three.js integration style alongside it would violate
constitution §7 (Convention Over Configuration) for no benefit — R3F is a thin renderer over Three.js,
not a limitation.

**Alternatives considered**: Raw `new THREE.WebGLRenderer()` imperative setup — rejected; would fork the
codebase's 3D-rendering convention in two directions with no functional need, purely for this feature.

## Decision 2: `AiPresenceCard`/decorative sphere are untouched (per user decision during planning)

**Decision**: `AiPresenceCard.tsx` and everything under `features/chat/scene/` are **not modified**. The
new viewer's placeholder content mode is a simple static background (an `aria-hidden` gradient/branded
box, similar in spirit to `SceneBackground`'s own `StaticFallback`, but without a Three.js canvas or the
decorative sphere).

**Rationale**: Investigation during planning found the decorative sphere is *not* currently a
full-viewport background — SPEC-024 deliberately relocated it into a small, independent, always-visible
corner presence card, distinct from `WorkspaceSurface.tsx` (which is a plain CSS gradient today, not a
Three.js scene). The user confirmed (clarification session, 2026-08-17) that this corner card stays
exactly as-is and is out of scope; the viewer's placeholder is a new, separate, non-sphere background.
Spec FR-004/FR-008/US2-AC3/SC-007 were updated accordingly in `/speckit-clarify`.

**Alternatives considered**: Promoting the sphere into the new viewer engine as its default content mode
— rejected by the user; would have meant either two simultaneous Three.js scenes (perf/visual
duplication) or retiring a recently-shipped, deliberately-placed UI element outside this feature's
intended scope.

## Decision 3: The GIS/map content mode is a separate `ViewerRenderTarget`, not a layer inside the R3F `<Canvas>`

**Decision**: Model the viewer's content modes behind a shared `IViewerEngine` facade with two
interchangeable render targets: `PlaceholderRenderTarget` (an R3F `<Canvas>`) and `MapRenderTarget`
(a Google Maps JS `<div>` hosting `google.maps.Map` + a `google.maps.WebGLOverlayView` that bridges its
own `THREE.Scene`/`THREE.PerspectiveCamera`/`THREE.WebGLRenderer` bound to the overlay's GL context, per
Google's documented `WebGLOverlayView` recipe). Only one render target is mounted at a time, matching
which content mode is active (spec: placeholder vs. map).

**Rationale**: `WebGLOverlayView` owns the Google Maps `<div>` and its own WebGL context/camera
projection (driven by the map's pan/zoom/tilt) — it cannot be embedded as a child layer inside an
unrelated R3F `<Canvas>`, and R3F's `<Canvas>` cannot be pointed at an externally-owned GL context
without fighting its render loop. Treating "which surface is currently visible" as a swappable adapter
behind one facade keeps `FR-002`'s "clear separation between viewer engine, camera/navigation, ...
render layers, GIS layers, model layers" honest at the architecture level, and keeps the *public*
command/event API (`contracts/viewer-engine-api.md`) identical regardless of which render target is
live — callers (including a future AI agent) never need to know which one is mounted.

**Alternatives considered**: Rendering the map as a flat 2D `<img>`/static-tile background instead of
`WebGLOverlayView` — rejected; the user explicitly named `WebGLOverlayView` for this feature. Embedding
Google Maps as an iframe-like overlay *above* the R3F canvas with no Three.js bridging — rejected; loses
the "3D geographic environments"/composability goal (FR-003) since nothing could be layered on top of
raw Maps tiles.

## Decision 4: Isometric/plan toggle repurposes the existing `viewMode` control, not a new one

**Decision**: The existing `workspaceOverlayStore.viewMode: '2D' | '3D'` field and its
`useViewModeControl()` `ExpandableActionGroup` (currently a **cosmetic no-op** — its own code comment
says it only picks a different background gradient angle, "this feature does not implement real spatial
rendering") are repurposed into the real isometric/plan camera toggle. The type is renamed
`viewMode: 'isometric' | 'plan'` (the store already isn't persisted, so this is a clean rename, not a
migration) and wired to the viewer engine's camera command instead of a decorative gradient.

**Rationale**: `WorkspaceSurface.tsx`'s own comment describes this control as a placeholder for exactly
this future capability. Building a second, competing toggle would duplicate `ControlDefinition`
UI/store plumbing that already exists in the right place (`right-stack` placement, `RiMapLine`/
`RiBox3Line` icons already imported) — a direct case for constitution §3 DRY/YAGNI.

**Alternatives considered**: A brand-new `isometricPlanControl` alongside the untouched `viewModeControl`
— rejected as duplicative; would leave the old placeholder inert and confusing next to a new real one.

## Decision 5: Rotation toggle is a standalone `Fab`, following the `ThemeToggleButton` pattern

**Decision**: A new `RotationToggleButton` component, styled identically to `ThemeToggleButton.tsx`
(reusing `CIRCULAR_ACTION_CHROME`, no expand/collapse state), added to `WorkspaceOverlay`'s
`topClusterLeading` slot alongside the theme toggle.

**Rationale**: Rotation start/stop is an instant, binary, always-visible action with no sub-menu — the
same shape as the theme toggle, not a disclosure widget like the six `ControlDefinition`s. Matches the
UI convention already established for this exact interaction shape in this codebase.

**Alternatives considered**: Adding rotation as a third item inside the repurposed view-mode
`ExpandableActionGroup` — rejected; rotation is orthogonal to camera perspective (spec FR-014: "starts
and stops... *independently* of the view-mode control") and forcing it into the same disclosure group
would conflate two independent controls the spec deliberately keeps separate.

## Decision 6: Weather provider is a keyless REST API, still proxied through the backend

**Decision**: Use a provider that requires no API key for the expected usage volume (e.g. Open-Meteo's
free forecast API, chosen for its keyless access and standard WMO weather-interpretation codes) behind
a new `IWeatherProvider` interface (`Application/Abstractions`) with a single `WeatherProvider`
implementation (`Infrastructure/Weather`). The backend still proxies the call (spec FR-012a) — not for
secret custody in this case (there is no secret), but to keep rate-limiting, structured logging, and
error-shape consistency (constitution §6/§14) uniform with every other external call in this codebase,
and so swapping to a paid, richer provider later (per `IWeatherProvider`'s abstraction) requires zero
frontend changes.

**Rationale**: Matches spec Assumptions ("a provider requiring no cost for expected usage volumes is
preferred by default") while still satisfying the clarified hybrid architecture (map client-side,
weather backend-mediated) and constitution §7's "swapping a provider MUST be achievable by adding a new
Infrastructure implementation... zero changes to Application/Domain."

**Alternatives considered**: A paid provider (e.g. OpenWeatherMap) with a server-held key — rejected as
the default; still fully supported later via the same interface if richer condition data is ever
needed, at which point `WeatherOptions.ApiKey` (bound the same way as `PineconeOptions`/
`GoogleGeminiOptions`) would be populated via environment/user-secrets, never appsettings.json.

## Decision 7: Weather condition → icon mapping is a small internal enum, not the provider's raw codes

**Decision**: `WeatherProvider` translates the upstream provider's raw condition codes into a small,
stable `WeatherCondition` enum (`Clear`, `PartlyCloudy`, `Cloudy`, `Fog`, `Rain`, `Snow`,
`Thunderstorm`, `Windy`) plus an `IsDaytime` flag, returned to the frontend as part of
`WeatherSnapshotDto`. The frontend maps this enum to Remix Icon components.

**Rationale**: Decouples the frontend icon set from any specific provider's code taxonomy (constitution
§7 Infrastructure isolation — swapping providers later must not require a frontend change), and matches
the reference image's condition categories (sun, sun+cloud, cloud, rain, snow, thunder, night variants,
wind) with a minimal, closed set (constitution §4 "C# `enum` is used for closed, stable sets").

**Alternatives considered**: Passing the provider's raw code straight through to the frontend — rejected;
couples the UI to a specific vendor's taxonomy and violates the provider-abstraction discipline used
everywhere else in this codebase (constitution §9).

## Decision 8: Geolocation uses standard (not high) accuracy, and no reduced-motion special-case beyond FR-016

**Decision**: `navigator.geolocation.getCurrentPosition` is called with `enableHighAccuracy: false` and a
reasonable timeout (e.g. 10s), matching FR-006's "standard, permission-based mechanism."

**Rationale**: City/neighborhood-level precision is sufficient for centering a map and looking up
weather; requesting high accuracy increases permission-prompt friction, latency, and (on some devices)
battery/GPS cost for no user-visible benefit here.

**Alternatives considered**: High-accuracy GPS — rejected as unnecessary for this feature's precision
needs.

## Decision 9: Existing placeholder toolbar controls (`layers`, `navigation`, `selection`, `analysis`) are wired to real viewer commands, not replaced

**Decision**: `workspaceControls.tsx`'s already-defined `layersControl`, `navigationControl` (its "My
location" action is currently a `comingSoon('Navigation')` stub), `selectionControl`, and
`analysisControl` `ControlDefinition`s are wired to real `viewer/api` commands (`addLayer`/
`zoomToLocation`/`select`/`createOverlay` respectively) instead of their current placeholder behavior,
reusing their existing icons (`RiGpsLine`, etc.) and toolbar placement.

**Rationale**: These slots already exist, in the right positions, with the right icons, specifically
reserved for this kind of capability — building new ones would duplicate UI shell that constitution §3/
§7 require reusing.

**Alternatives considered**: Leaving these controls as `comingSoon` stubs and adding new ones — rejected;
would leave dead UI next to working duplicates.

## Decision 10: Frontend testability boundary around `WebGLOverlayView`

**Decision**: Unit/component tests (`vitest` + RTL) cover the pure, framework-independent logic —
`viewerEngineStore` state transitions, the command/event contract (`viewer/api`), the weather
condition→icon mapping, and `useGeolocation`'s permission-state handling (via a mocked
`navigator.geolocation`) — using `msw` to mock the weather HTTP call. The actual Google Maps
`WebGLOverlayView` rendering is **not** unit-tested (no real WebGL/Maps JS runtime in `jsdom`);
correctness there is verified via the Playwright E2E smoke test and manual `quickstart.md` validation.

**Rationale**: Matches the existing, already-accepted boundary in this codebase — `SceneBackground`'s
own tests focus on quality-tier/fallback *logic*, not actual WebGL pixel output, for the same reason.

**Alternatives considered**: Attempting to mock `google.maps.WebGLOverlayView` deeply enough to unit-test
real rendering — rejected as low-value, high-maintenance test theater for a browser API this codebase
has no existing pattern for stubbing.

## Output

All unknowns from Technical Context are resolved. No `NEEDS CLARIFICATION` markers remain. Proceeding
to Phase 1 (data-model.md, contracts/, quickstart.md).
