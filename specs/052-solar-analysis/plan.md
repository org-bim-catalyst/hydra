# Implementation Plan: Solar Analysis

**Branch**: `052-solar-analysis` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/052-solar-analysis/spec.md`

## Summary

Sun path, real building shadows and time scrubbing over a site, built entirely as a viewer
extension on the frameworks specs/049–051 delivered.

The solar calculation runs in the browser from the reference implementation's NOAA code, ported
into a tested module with **no runtime dependency added** (research D1), positioned through
specs/051's published `worldToLocal` conversion inside a Drawing Space the framework owns and tears
down. Building footprints come from OSM Overpass **through the platform** — a new
`IBuildingFootprintProvider` mirroring specs/042's `IBoundaryCandidateProvider` exactly, behind a
new read endpoint. They cast shadows but are **not drawn**, because the basemap already draws its
own 3D buildings and a second misaligned copy reads as a rendering bug (research D13; FR-010
amended accordingly). Shadows use a `DirectionalLight` plus a `ShadowMaterial` ground plane sized
together with the shadow frustum from one radius (research D16), enabled by *declaring* the
`shadows` and `toneMapping` drawing requirements rather than touching the renderer. Lucy opens the
analysis through the same trailing-SSE mechanism specs/051 proved twice, and deliberately does not
recompute the figures.

The feature's second purpose is to test SC-009: that it needs no change to the viewer, extension or
panel frameworks. Two risks to that are identified up front — the missing `onFrame` unsubscribe
(research D9) and the absence of an `environment` drawing requirement (research D17).

**The reference implementation** (`sunpath-osm-shadows-13.html`) was read in full and changed this
plan in six places — research D1, D5, D13, D14, D15, D16 and D17 all carry what it established.

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript 5.x strict, React 19 (frontend)

**Primary Dependencies**: Existing — Three.js 0.185, Zustand 5, MUI, TanStack Query, zod, MediatR,
FluentValidation. **New (frontend)** — `tz-lookup` only (CC0, ~150 KB, offline lat/lng → IANA time
zone, lazy-loaded behind the extension). Solar position is ported source, not a dependency
(research D1).

**Storage**: None. Corrections are session-scoped Zustand state and are explicitly not persisted
(spec Out of Scope). Building footprints are fetched per request; no new tables, no migration.

**Testing**: Vitest + Testing Library + jest-axe (frontend); xUnit + NSubstitute + FluentAssertions
(backend). Frontend type-check is `tsc -b --noEmit` (bare `tsc --noEmit` is a no-op in this repo).

**Target Platform**: Modern evergreen browsers with WebGL2, behind the viewer's existing
`useWebGLSupport` gate; ASP.NET Core on Windows/site4now hosting.

**Project Type**: Web application — React SPA (`src/AskLucy.Web/ClientApp`) over a Clean
Architecture .NET backend.

**Performance Goals**: Scrubbing and playback hold ≥30 fps with the analysis active (SC-004) —
"reads as continuous motion". Redraws happen only via `redrawScheduler.invalidate()`; no permanent
redraw loop. Building retrieval completes inside the existing Overpass client budget (30 s client
timeout, `[timeout:25]` server-side, 3 attempts across mirrors).

**Constraints**: Shadow-map work competes for the GPU with the Google Maps WebGL bridge — a
measured hazard in this codebase (`sphere_perf_was_actually_map_gpu_contention`). Shadow map
resolution and the orthographic shadow camera are therefore sized to the analysis area, not the
world. Turning `shadowMap.enabled` on is a global, visually-observable change to already-tuned
layers (memory constraint 4) and is treated as its own deliberate step with before/after review.

**Scale/Scope**: Analysis radius bounded (default **200 m**, the prototype's proven working value),
building count bounded (default 300, with a user-visible "data was limited" signal). 5 user
stories, 46 functional requirements, 10 success criteria.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Pre-design evaluation — PASS.**

| Principle | Assessment |
|---|---|
| §2.I Clean Architecture & Dependency Rule | New backend code is `Application` (interface + DTO + query handler) → `Infrastructure` (Overpass implementation) → `Web` (controller). `Application` defines `IBuildingFootprintProvider`; `Infrastructure` implements it. No inward-pointing dependency. Mirrors specs/042's proven shape exactly. |
| §2.II SOLID | `IBuildingFootprintProvider` is narrow and single-purpose (ISP/SRP). A future cadastral/authoritative source is an additive `Infrastructure` implementation (OCP/DIP), not a rewrite. |
| §2.III DRY/KISS/YAGNI | The Overpass HTTP client, retry, mirror-failover and timeout policy are **reused**, not re-derived. No abstraction is introduced for a second solar provider that does not exist. Quantitative analysis is explicitly out of scope and no seam is built for it. No dependency is added where the project already has working code (research D1), and the prototype's decorative glass dome is dropped rather than carried (research D17). |
| §2.IV Composition over inheritance | The extension is a plain object literal satisfying `ViewerExtension`, matching every existing builtin — no base class. |
| §2.V DI & testability | Provider injected via constructor; the query handler is unit-testable with a faked provider, no network. Solar math is pure functions over `(lat, lng, instant)`, testable with zero rendering. |
| §2.VI Separation of concerns | Solar math, building retrieval and rendering are three separate modules. React components hold no astronomy. The controller holds no business logic (MediatR query). |
| §2.VII Convention over configuration | Follows the established extension/builtin shim + `features/<domain>` split, the `__TAG__` trailing-SSE capability pattern, and the `IBoundaryCandidateProvider` provider shape. |
| §2.VIII No Silent Failures (NON-NEGOTIABLE) | FR-045/FR-046 and SC-008 restate this requirement at feature level. Every failure path — Overpass unavailable, no buildings, time zone undetermined, WebGL unsupported, an unusable footprint, an invalid correction — has a named user-visible surface. The extension's `onFrame` callback is already contained by the framework (specs/051 `invokeFrameCallbacks`) and surfaced through `ExtensionFailureNotice`. Building retrieval is a TanStack Query with an explicit error branch rendered in the panel, never console-only. |
| §7 UI / WCAG 2.1 AA | Controls are MUI, keyboard-operable, in panels the existing framework already makes accessible; figures are panel **content** (FR-031) so they inherit `ContentRenderer`'s accessibility. jest-axe covers the new panels. Per the spec's own Assumption, the 3D display is an enhancement and the figures carry the information textually. |
| §10 Testing | Unit tests for solar math against published reference values, for footprint parsing/exclusion, and for the provider; component + a11y tests for the panels; teardown tests for SC-006/SC-007 mirroring specs/051's fifty-cycle test. |
| §15 Performance | `tz-lookup` is lazy-loaded behind the extension (§7 "large dependencies are lazy-loaded behind the feature that needs them"), not in the initial bundle. Porting NOAA rather than adding `astronomy-engine` keeps ~230 KB out of the build entirely. Building meshes skip the colour and depth passes (research D13), so they cost only the shadow pass. |

**No violations. Complexity Tracking table is empty.**

**Post-design re-evaluation (after Phase 1, and after reading the reference implementation) —
PASS.** The contracts introduce one new backend interface, one new Infrastructure implementation,
one new read endpoint, one new agent capability and one new frontend feature module. No new
datastore, no new cross-cutting pattern, no change to an existing public contract — so no ADR is
required under §17. Two design risks to SC-009 are recorded with stated escalation paths rather
than absorbed silently: research D9 (`onFrame` has no unsubscribe) and research D17 (no
`environment` drawing requirement, which is why the prototype's glass dome is dropped rather than
reproduced by reaching for global scene state).

One spec change was required and was taken deliberately, not quietly: **FR-010 and US2 acceptance
scenario 1 were amended** so that building footprints cast shadows without being drawn (research
D13). This was confirmed with the user before the edit, and a new Clarifications session records
both the question and the reasoning.

## Project Structure

### Documentation (this feature)

```text
specs/052-solar-analysis/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── building-footprints-endpoint.md
│   ├── solar-extension.md
│   ├── solar-panels.md
│   └── open-solar-analysis-capability.md
├── checklists/
│   └── requirements.md  # Already complete
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/AskLucy.Application/
├── Buildings/                                  # NEW — mirrors SiteBoundaries/ exactly
│   ├── IBuildingFootprintProvider.cs
│   ├── BuildingFootprint.cs
│   ├── BuildingFootprintResult.cs
│   ├── BuildingProviderUnavailableException.cs
│   └── Queries/GetSiteBuildings/
│       ├── GetSiteBuildingsQuery.cs
│       ├── GetSiteBuildingsQueryHandler.cs
│       └── GetSiteBuildingsQueryValidator.cs
└── Conversations/Capabilities/
    └── OpenSolarAnalysisCapability.cs          # NEW — mirrors LoadViewerContentCapability

src/AskLucy.Infrastructure/
└── Buildings/                                  # NEW
    ├── OverpassBuildingFootprintProvider.cs    # reuses the "Overpass" HttpClient + mirrors
    └── BuildingRetrievalOptions.cs

src/AskLucy.Web/
├── Controllers/v1/SiteBuildingsController.cs   # NEW — GET /api/v1/site-buildings
└── ClientApp/src/
    ├── features/solar/                         # NEW — the capability's own module
    │   ├── api/siteBuildingsApi.ts
    │   ├── solar/{solarPosition,daySummary,sunPath,timeZone}.ts
    │   ├── buildings/{footprintGeometry,buildingHeights}.ts
    │   ├── scene/{SolarScene,sunLight,shadowGround,sunPathCurve}.ts
    │   ├── store/{solarAnalysisStore,correctionsStore}.ts
    │   ├── panels/{SolarTimeControlPanel,BuildingCorrectionsPanel,solarFiguresContent}.tsx
    │   └── components/SolarAnalysisOverlay.tsx
    └── viewer/extensions/
        ├── builtin/solarAnalysisExtension.tsx  # NEW — thin shim, registers + wires context
        └── declared.ts                          # MODIFIED — one added id

tests/
├── AskLucy.Application.Tests/Buildings/…       # NEW
├── AskLucy.Application.Tests/Conversations/Capabilities/OpenSolarAnalysisCapabilityTests.cs
└── AskLucy.Infrastructure.Tests/Buildings/…    # NEW
```

**Structure Decision**: The capability lives in `ClientApp/src/features/solar/` (constitution §4:
frontend organized by feature-domain under `src/features/<domain>`), with only a thin registration
shim under `viewer/extensions/builtin/`. This is the established split — `siteBoundaryExtension.tsx`
already imports its component from `features/viewer/`. Keeping the body of the feature outside
`src/viewer/` is deliberate: SC-009 asserts this feature changes nothing in the viewer core, and
code sitting inside `src/viewer/` would make that claim harder to verify. Backend additions mirror
`Application/SiteBoundaries` + `Infrastructure/Boundaries` one-for-one so the Overpass retry,
mirror-failover and timeout behaviour is reused rather than re-derived.

## Complexity Tracking

> No Constitution Check violations. Table intentionally empty.
