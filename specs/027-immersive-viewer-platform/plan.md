# Implementation Plan: Immersive Viewer Platform for AI-Assisted Urban Design

**Branch**: `027-immersive-viewer-platform` | **Date**: 2026-08-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/027-immersive-viewer-platform/spec.md`

## Summary

Replace the main Flumeria workspace's flat gradient background (`WorkspaceSurface.tsx`) with an
extensible, layered Three.js viewer engine that occupies the majority of the viewport. The viewer
starts on a simple static placeholder, then — once the user's location resolves via the browser
Geolocation API — switches to a Google Maps `WebGLOverlayView` GIS layer centered on that location.
A weather widget (location name, temperature, condition icon) appears alongside it, backed by a new
backend-proxied weather endpoint. The workspace toolbar gains an isometric/plan camera-view toggle
(repurposing the existing, currently-inert `viewMode` control) and a rotation start/stop toggle. The
viewer exposes a documented, typed command/event API (add/remove layer, zoom to location,
select/highlight, display content, create overlay) that this feature exercises directly — no AI
agent integration is built here, only the contract later features will call. The existing decorative
sphere and its corner presence card (`AiPresenceCard`, SPEC-024) are explicitly out of scope and
unaffected.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend); TypeScript ~6.0 with React 19, Vite 8 (frontend,
`src/AskLucy.Web/ClientApp`)

**Primary Dependencies**:
- Frontend (existing, reused): `three` ^0.185.1, `@react-three/fiber` ^9.6.1, `@react-three/drei`
  ^10.7.7, `zustand` ^5, `@tanstack/react-query` ^5, `@mui/material` ^9, `@remixicon/react` ^4.9
- Frontend (new): a Google Maps JavaScript API loader (`@googlemaps/js-api-loader` or equivalent) and
  `@types/google.maps`, loaded only when the map content mode activates (lazy/code-split, per
  constitution §7 "large dependencies are lazy-loaded behind the feature that needs them")
- Backend (existing, reused): MediatR, FluentValidation, `IHttpClientFactory`,
  `Microsoft.AspNetCore.RateLimiting`
- Backend (new): none — the weather integration follows the existing `PineconeOptions`/`AddHttpClient`
  pattern (simple `IOptions<T>`-bound HTTP client), no new NuGet package required if the chosen
  provider (research.md Decision 6) is a plain REST/JSON API

**Storage**: SQL Server (existing, unaffected). This feature introduces **no new persistent storage**
— resolved location and weather are session/client-state only (spec FR-012b).

**Testing**: `vitest` + `@testing-library/react` + `msw` + `jest-axe` (frontend unit/component/a11y,
existing convention); `xUnit` + `NSubstitute` + `FluentAssertions` (backend unit/integration, existing
convention, `StubHttpMessageHandler` pattern for HTTP-client tests); Playwright `.spec.ts` (E2E,
existing convention, `tests/AskLucy.E2E.Tests`)

**Target Platform**: Web browser (desktop/tablet/mobile) via the existing ASP.NET Core–hosted React
SPA; no new deployment target.

**Project Type**: Web application (existing `AskLucy.Domain/Application/Infrastructure/Persistence/Web`
solution + `ClientApp` React frontend) — this feature adds to the existing structure, no new project.

**Performance Goals**: Viewer sustains ~60fps once the map/GIS layer is active on typical modern
hardware, gracefully degrading on lower-end devices (spec FR-005a); camera-control interactions
respond in <300ms (spec SC-004); the weather endpoint responds in <500ms p95 excluding upstream
provider latency, consistent with existing endpoint expectations.

**Constraints**: No server-side persistence of location/weather (FR-012b); Google Maps key is a
domain-restricted public key used client-side, weather credentials (if any) stay server-side
(FR-012a, constitution §8); the existing decorative-sphere presence card (`AiPresenceCard`) MUST NOT
be touched or regressed (FR-004, SC-007); every public endpoint MUST be rate-limited (constitution
§6).

**Scale/Scope**: Single-user, session-scoped viewer instance per browser tab; no multi-user
concurrency concerns beyond the existing per-user rate limiting; 6 user stories (2×P1 camera/platform
foundation, 1×P1 location/map, 2×P2 weather/selection, 1×P3 programmatic API).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Clean Architecture & Dependency Rule | PASS — new weather capability follows `Application` interface (`IWeatherProvider`) → `Infrastructure` implementation → `Web` controller; the viewer engine is presentation-only (no business logic), matching how `SceneBackground`/`ReactiveSphere` already work. |
| II. SOLID | PASS by design — viewer engine split into single-responsibility modules (engine, camera, layers, selection, overlays, command/event bus), each independently extensible (OCP) without modifying existing ones. |
| III. Simplicity First (DRY/KISS/YAGNI) | PASS, and actively reduces scope — research.md Decisions 4–6 repurpose *existing* placeholder toolbar controls (`viewMode`, `layersControl`, `navigationControl`, `selectionControl`, `analysisControl` in `workspaceControls.tsx`) instead of building new UI shells; only the isometric/plan semantics, rotation toggle, and their real wiring are new. |
| IV. Composition Over Inheritance | PASS — render layers (GIS/model/overlay) are composed via a shared `RenderLayer` interface + a `ViewerRenderTarget` adapter (research.md Decision 3), not a class hierarchy. |
| V. Dependency Inversion & Testability | PASS — `IWeatherProvider` is defined in `Application`, mockable in handler unit tests with no network; viewer command/event logic is pure state-machine code testable without a real WebGL context. |
| VI. Separation of Concerns | PASS — weather validation/orchestration lives in an `Application` MediatR query, not the controller; the React `WeatherController`-equivalent (`WeatherController.cs`) only translates HTTP ↔ MediatR. |
| VII. Convention Over Configuration | PASS — weather HTTP client registration mirrors the existing `PineconeOptions`/named-`HttpClient` pattern; rate-limit policy mirrors the existing per-feature `AddPolicy` pattern in `Program.cs`; Zustand store follows the session-scoped, non-persisted `workspaceOverlayStore` pattern (not `themeStore`'s `persist`), consistent with FR-012b. |
| VIII. No Silent Failures (NON-NEGOTIABLE) | PASS with one documented, precedent-based carve-out — see note below. |
| §6 API Standards | PASS — new `GET /api/v1/weather/current` is versioned, rate-limited (`weather-endpoints` policy), returns RFC 9457 Problem Details on failure via the existing `ProblemDetailsMiddleware`, documented in OpenAPI automatically (attribute-routed MVC controller). |
| §7 UI Principles | PASS — new toolbar controls reuse `CIRCULAR_ACTION_CHROME`/`ExpandableActionGroup`/`Fab` patterns, MUI theming, and MUST meet WCAG 2.1 AA (keyboard operability, ARIA, contrast) exactly as `CircularAction`/`ThemeToggleButton` already do — carried forward into tasks even though the spec (a business-facing document) doesn't restate it, since the constitution governs it unconditionally. |
| §8 Security | PASS — geolocation is permission-gated (browser-native prompt, no silent collection); Google Maps key is domain-restricted (not a secret); weather lookup is backend-mediated for rate-limiting/observability consistency (research.md Decision 6); no PII persisted (FR-012b). |
| §9 AI Principles | N/A / PASS — this feature deliberately stops at the command/event *contract* (FR-024); no agent is wired to call it, consistent with "Agent architecture… scoped tool set" being a later feature's concern. |
| §14 Observability | PASS — weather calls go through the same Serilog structured-logging + correlation-id pipeline as every other endpoint; viewer events are an in-browser pub/sub for now (not wired to backend telemetry — noted as an intentionally deferred, low-impact item during `/speckit-clarify`). |
| §15 Performance | PASS by design — 60fps target stated explicitly (FR-005a); large Google Maps loader dependency is lazy-loaded only when the map layer activates. |

**No Silent Failures — documented carve-out**: Per spec FR-008/Edge Cases (resolved in `/speckit-clarify`), a denied/unavailable geolocation permission and an unreachable weather/map provider degrade the viewer to its placeholder background and hide the weather widget **without a toast or inline error**. This mirrors the *already-accepted* precedent in this exact codebase: `SceneBackground.tsx`'s `SceneErrorBoundary` deliberately does not toast on a decorative-rendering failure (only `console.error`s for telemetry) because the failure isn't user-actionable. The weather widget and map layer are the same category — ambient, supplementary content, not a user-initiated action the user is left wondering about. This is **not** a violation of constitution §2.VIII (that principle's own rationale targets actionable operations like sends/fetches the user is waiting on); it is applied consistently with the one carve-out this codebase has already established, not a new one. No Complexity Tracking entry is needed because no principle is actually being broken — this is documented here for reviewer visibility per the constitution's "explain architectural trade-offs" rule (§18).

**Post-Phase-1 re-check**: Phase 0 (research.md) and Phase 1 (data-model.md, contracts/,
quickstart.md) are complete. No new violation surfaced during design — the render-target adapter
(research.md Decision 3), the `IWeatherProvider` abstraction, and the reused toolbar controls all
reinforce the gates above rather than straining them. The single documented carve-out (no toast on
ambient location/weather/map failure) is unchanged and remains a consistent application of the
existing `SceneBackground` precedent, not a new one. Gate: **PASS**.

## Project Structure

### Documentation (this feature)

```text
specs/027-immersive-viewer-platform/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── viewer-engine-api.md
│   └── weather-api.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── AskLucy.Domain/                          # unaffected — no new domain entities (FR-012b: no persistence)
├── AskLucy.Application/
│   └── Weather/
│       ├── WeatherSnapshotDto.cs
│       └── Queries/GetCurrentWeather/
│           ├── GetCurrentWeatherQuery.cs
│           ├── GetCurrentWeatherQueryHandler.cs
│           └── GetCurrentWeatherQueryValidator.cs
├── AskLucy.Infrastructure/
│   ├── DependencyInjection.cs               # + AddHttpClient("Weather", ...) + AddOptions<WeatherOptions>
│   └── Weather/
│       ├── WeatherOptions.cs
│       └── WeatherProvider.cs               # implements IWeatherProvider (Application/Abstractions)
├── AskLucy.Persistence/                     # unaffected
└── AskLucy.Web/
    ├── Program.cs                            # + "weather-endpoints" rate-limit policy
    ├── Controllers/v1/
    │   └── WeatherController.cs              # GET /api/v1/weather/current
    └── ClientApp/src/
        ├── viewer/                           # NEW — the extensible viewer engine (framework-agnostic core + R3F host)
        │   ├── engine/                       # ViewerEngine facade, ViewerRenderTarget adapter (placeholder vs. map)
        │   ├── camera/                       # isometric/plan view-mode + rotation state machine
        │   ├── layers/
        │   │   ├── gis/                      # GoogleMapsGisLayer (WebGLOverlayView bridge)
        │   │   └── model/                    # RenderLayer contracts only (no real content this feature)
        │   ├── selection/                    # selection/highlight state + resolution rules
        │   ├── overlays/                     # Overlay contracts
        │   ├── api/                          # ViewerCommand / ViewerEvent typed contracts (contracts/viewer-engine-api.md)
        │   └── store/
        │       └── viewerEngineStore.ts       # zustand, session-scoped (no persist), mirrors workspaceOverlayStore pattern
        └── features/
            ├── viewer/                        # NEW — feature-level UI wiring
            │   ├── api/weatherApi.ts
            │   ├── hooks/
            │   │   ├── useGeolocation.ts
            │   │   └── useCurrentWeather.ts
            │   └── components/
            │       ├── ViewerSurface.tsx       # replaces WorkspaceSurface's gradient with the mounted viewer
            │       ├── LocationWeatherWidget.tsx
            │       └── RotationToggleButton.tsx
            └── chat/
                ├── components/
                │   ├── workspaceControls.tsx    # MODIFIED — wire viewMode/layers/navigation/selection/analysis to real viewer commands
                │   └── WorkspaceSurface.tsx      # MODIFIED or replaced by ViewerSurface (research.md Decision 1)
                └── pages/ChatPage.tsx            # MODIFIED — mount ViewerSurface, AiPresenceCard untouched (FR-004)

tests/
├── AskLucy.Application.Tests/Weather/GetCurrentWeatherQueryHandlerTests.cs
├── AskLucy.Infrastructure.Tests/Weather/WeatherProviderTests.cs
├── AskLucy.Web.Tests/Controllers/WeatherControllerTests.cs
├── AskLucy.E2E.Tests/ImmersiveViewerPlatform.spec.ts
└── (frontend) co-located *.test.tsx / *.a11y.test.tsx next to each new component, matching existing convention
```

**Structure Decision**: Existing single-solution web-application layout (Clean Architecture backend +
`ClientApp` React frontend) is reused as-is — no new project, no new solution folder. The viewer
engine is a new, self-contained `ClientApp/src/viewer/` package (framework-agnostic state/contracts,
with a thin React/R3F host), kept separate from `features/viewer/` (the feature-level UI that wires
the engine into the workspace page), so the engine itself stays reusable/testable independent of this
specific page's layout — matching the spec's own "clear separation between viewer engine, camera,
layers, selection, overlays, API" requirement (FR-002).

## Complexity Tracking

*No entries — no Constitution Check gate was violated (see the documented, precedent-based carve-out
under "No Silent Failures" above, which is a consistent application of an existing pattern, not a new
deviation).*
