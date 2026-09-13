# Tasks: Solar Analysis

**Input**: Design documents from `/specs/052-solar-analysis/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included and **not optional** — constitution §10 requires tests for new behaviour in the
same change that introduces it, and SC-001/SC-002 are only claimable from tests.

**Organization**: Grouped by user story. US1 and US2 are both P1; US2 depends only on Foundational,
not on US1, so the two can proceed in parallel after Phase 2.

> **Revised after `/speckit-analyze`.** Remediations applied: rate limiting on the new endpoint
> (C1), Lucy's describe-the-display behaviour (H1), building-data caching (H2), shadow setup moved
> out of Foundational into US2 (M1/M2), SC-003 coverage (M3), inter-building shadows (M4),
> centralized copy (M5), and the measured accuracy tolerance linked to the figures wording (M6).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)
- Exact file paths are given in every task

## Path Conventions

- Frontend feature module: `src/AskLucy.Web/ClientApp/src/features/solar/`
- Extension shim: `src/AskLucy.Web/ClientApp/src/viewer/extensions/builtin/`
- Backend: `src/AskLucy.Application/`, `src/AskLucy.Infrastructure/`, `src/AskLucy.Web/`
- Backend tests: `tests/AskLucy.Application.Tests/`, `tests/AskLucy.Infrastructure.Tests/`
- Frontend tests live beside their subject (`*.test.ts` / `*.test.tsx`), per existing convention

**Verification commands** (run continuously, not only at the end):

```bash
cd "src/AskLucy.Web/ClientApp" && npx tsc -b --noEmit && npm test   # NOT bare `tsc --noEmit`
dotnet build "Ask Lucy.sln"                                         # note the space in the name
```

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Dependencies and module skeleton.

- [X] T001 Add `tz-lookup` to dependencies in `src/AskLucy.Web/ClientApp/package.json` and install (research D2 — the only new runtime dependency; solar position is ported source, not a package)
- [X] T002 [P] Create the feature module folder skeleton under `src/AskLucy.Web/ClientApp/src/features/solar/` — `api/`, `solar/`, `buildings/`, `scene/`, `store/`, `panels/`, `components/` per plan.md Project Structure
- [X] T003 [P] Create `src/AskLucy.Application/Buildings/` and `src/AskLucy.Infrastructure/Buildings/` folders mirroring the existing `SiteBoundaries`/`Boundaries` layout

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Solar mathematics, site/time primitives, centralized copy, the analysis store, and the
extension shell. Every user story depends on these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

> Shadow-specific scene work (light, ground plane) deliberately lives in US2, not here — US1 needs
> no building data and no shadows, and keeping them out is what makes the MVP claim below true.

### Solar mathematics (research D1)

- [X] T004 [P] Port the reference implementation's NOAA solar-position algorithm into `src/AskLucy.Web/ClientApp/src/features/solar/solar/solarPosition.ts` — `solarPosition(instantUtc, latitude, longitude) → { azimuthDegrees, altitudeDegrees, declination, eqTime }`, pure, no rendering imports (FR-001)
- [X] T005 [P] Implement `src/AskLucy.Web/ClientApp/src/features/solar/solar/daySummary.ts` — `daySummary(date, latitude, longitude) → DaySummary` per data-model.md, distinguishing `polarCondition` `'midnight-sun'` vs `'polar-night'` via the `cosH0 > 1` / `cosH0 < -1` bounds plus noon altitude (FR-002)
- [X] T006 Write `solarPosition.test.ts` and `daySummary.test.ts` in `features/solar/solar/` asserting against published NOAA Solar Calculator values at Quito, Dubai, London, Tromsø and Ushuaia across the year, at quickstart Scenario 1's tolerances — ±60 s rise/set within ±72° latitude (inherited), ±10 min beyond (inherited), ±0.1° azimuth/altitude (**measured**, labelled as such). **Export the verified tolerances as named constants from `solarPosition.ts`** (`SOLAR_POSITION_TOLERANCE_DEGREES`, `RISE_SET_TOLERANCE_SECONDS`) so the accuracy the user is shown in T018 is the same number the tests actually assert, and the two cannot drift apart (SC-001, FR-044)
- [X] T007 [P] Implement `src/AskLucy.Web/ClientApp/src/features/solar/solar/timeZone.ts` — lazy `tz-lookup` import, `resolveTimeZone(lat, lng) → { timeZoneId: string | null, timeBasisLabel: string }`, plus `toLocalParts`/`fromLocalParts` using `Intl.DateTimeFormat` with that zone (FR-003, FR-004, research D2)
- [X] T008 Write `timeZone.test.ts` covering London spring-forward and autumn-back, a southern-hemisphere transition, and a `tz-lookup` null returning the explicit "time zone could not be determined" basis label rather than a silent UTC default (SC-005, FR-003)

### Shared copy and state

- [X] T009 [P] Create `src/AskLucy.Web/ClientApp/src/features/solar/copy.ts` — every user-facing string this feature shows: buildings notices, failure wordings, polar/below-horizon statements, validation messages, and the stated-limits text. Constitution §7 requires copy be centralized rather than scattered as literals so i18n extraction is mechanical; every later panel/notice task sources its strings from here
- [X] T010 Implement `src/AskLucy.Web/ClientApp/src/features/solar/store/solarAnalysisStore.ts` — Zustand store holding `Site`, `AnalysisMoment`, `status`, `failureReason`, `buildingsNotice` per data-model.md; `instantUtc` canonical with `localDate`/`localMinuteOfDay` as projections through the site's zone (never the reverse — this is what keeps FR-004 correct)
- [X] T011 Write `solarAnalysisStore.test.ts` asserting that editing either local projection recomputes `instantUtc`, that `status: 'partial'` is not a failure state, and that every transition into `failed`/`partial` sets a user-facing string drawn from `copy.ts` (FR-045, SC-008)

### Extension shell and scene ownership

- [X] T012 Create `src/AskLucy.Web/ClientApp/src/viewer/extensions/builtin/solarAnalysisExtension.tsx` — `id: 'viewer.solar-analysis'`, manifest per contracts/solar-extension.md (`toggleable: true`, `startsWithViewer: false`), `start()` acquiring the drawing space and declaring **`toneMapping` only**, `activate()`/`deactivate()`, and a `stop()` that does nothing beyond stopping playback (all teardown is framework-owned — FR-037, FR-040). `shadows` is declared in US2 (T044), because declaring it flips a global renderer setting and research D6 requires that be its own reviewed step
- [X] T013 Register the extension id in `src/AskLucy.Web/ClientApp/src/viewer/extensions/declared.ts` (the only other file under `src/viewer/` this feature may touch — SC-009)
- [X] T014 Implement `src/AskLucy.Web/ClientApp/src/features/solar/scene/SolarScene.ts` — owns the extension's `DrawingSpaceHandle`, exposes `setGroundOffset(metres)` as `group.position.z` (research D15 — **never** re-anchor the scene), and a `disposeAll()` used only by tests (FR-038, FR-041)

**Checkpoint**: solar maths, time handling, copy, state and a conformant scene shell exist and are
tested. User stories can now begin.

---

## Phase 3: User Story 1 — See Where the Sun Is and Where It Goes (Priority: P1) 🎯 MVP

**Goal**: Sun path above the site for a chosen date, with seasonal extremes, hour marks, current
position, and the numeric figures in site-local time.

**Independent Test**: Open solar analysis on a site, choose a date, confirm the path and the
figures match an independent reference for that location and date. Needs no building data and no
shadows, so it works everywhere.

### Tests for User Story 1

- [X] T015 [P] [US1] Write `features/solar/scene/sunPathCurve.test.ts` asserting the chosen day's arc, both seasonal extremes and the hour marks are produced as distinct objects, and that arcs are built from tube geometry rather than `THREE.Line` (research D17 — WebGL draws lines at ~1 px regardless of requested width)
- [X] T016 [P] [US1] Write `features/solar/panels/solarFiguresContent.test.ts` asserting the composed document validates against `panelContentSchema`, always carries a "Times shown in" row, and always carries the design-stage-study / accuracy / assumed-heights statement (FR-043, FR-044, SC-010)

### Implementation for User Story 1

- [X] T017 [US1] Implement `src/AskLucy.Web/ClientApp/src/features/solar/scene/sunPathCurve.ts` — `sphericalToVec(az, alt, radius)` in ENU (X=East, Y=North, Z=Up), day-arc sampling, the chosen day's arc plus summer/winter extremes in distinguishable materials, and hour-mark sprites along the chosen day (FR-005, FR-006, FR-007). Dome radius 120 m. No `scene.environment` and no glass shell (research D17)
- [X] T018 [US1] Implement `src/AskLucy.Web/ClientApp/src/features/solar/panels/solarFiguresContent.ts` — composes the figures as specs/049 `metric`/`keyValue`/`text` blocks per contracts/solar-panels.md, sourcing wording from `copy.ts` and the stated accuracy from T006's exported tolerance constants (never a hand-typed number), including the polar-case and below-horizon wordings (FR-002, FR-017, FR-031, FR-043, FR-044)
- [X] T019 [US1] Wire the toolbar entry in `solarAnalysisExtension.tsx` via `context.contributeToolbarEntry(...)` — opens the analysis, and is withdrawn by the framework on stop (FR-029)
- [X] T020 [US1] Open the figures panel through `context.openPanel({ kind: 'content', ... })` on activate, refreshing its content whenever `instantUtc`, the site, or the buildings change (FR-008, FR-031)
- [X] T021 [US1] Implement site following in `src/AskLucy.Web/ClientApp/src/features/solar/components/SolarAnalysisOverlay.tsx` — subscribe to `activeLocationStore`; on site change recompute time zone, sun path and figures for the new site; close only when the viewer has no site at all (FR-008, FR-042, US1 scenario 6, research D10)
- [X] T022 [US1] Call `drawingSpace.invalidate()` after every state change that alters what is drawn, and **verify whether one coalesced redraw is sufficient** — if the change does not appear until the user interacts, use repeated `invalidate()` across successive animation frames, never `requestRedraw()` (research D14, FR-039)
- [X] T023 [US1] Write `features/solar/components/SolarAnalysisOverlay.test.tsx` covering open → path visible → date change updates path → site change follows → no-site closes (US1 scenarios 1–6, FR-008)

**Checkpoint**: User Story 1 is fully functional and independently testable. **This is the MVP** —
and it genuinely needs nothing from Phase 4.

---

## Phase 4: User Story 2 — See Real Shadows at a Moment (Priority: P1)

**Goal**: Shadows cast by the actual surrounding buildings, at their real heights, onto the ground
and onto each other.

**Independent Test**: Open on a site with known surrounding buildings, set a known date and time,
confirm shadows fall in the correct direction and at plausible lengths against an independent
reference.

### Backend — building retrieval (contracts/building-footprints-endpoint.md)

- [X] T024 [P] [US2] Create `src/AskLucy.Application/Buildings/BuildingFootprint.cs` and `BuildingFootprintResult.cs` per data-model.md — `Id`, `Ring`, `HeightMetres`, `HeightProvenance`, `Name`, `IsSiteBuilding`; result carries `Buildings`, `Limited`, `ExcludedCount`, `RadiusMetres`
- [X] T025 [P] [US2] Create `src/AskLucy.Application/Buildings/IBuildingFootprintProvider.cs` and `BuildingProviderUnavailableException.cs`, mirroring `IBoundaryCandidateProvider`/`BoundaryProviderUnavailableException` exactly (research D4)
- [X] T026 [US2] Implement `src/AskLucy.Infrastructure/Buildings/OverpassBuildingFootprintProvider.cs` — query `way["building"](around:R,lat,lng);out geom;`, **reusing the existing `"Overpass"` named `HttpClient`** so the 3-attempt retry, mirror rotation and 30 s timeout are inherited rather than re-derived; skip `relation` elements for v1 as the boundary provider does (FR-009)
- [X] T027 [US2] Implement height resolution and footprint exclusion inside the provider — `height` tag → `known`; `building:levels` × 3.0 m → `assumed`; default **9.0 m** → `assumed` (research D5); exclude rings with < 4 points, non-closed, near-zero area or self-intersecting, counting them into `ExcludedCount` (FR-010, FR-011, FR-013)
- [X] T028 [US2] Implement the site-building rule in the provider — containment first, then nearest edge within **25 m**, otherwise none; at most one `IsSiteBuilding: true` (FR-012, research D8)
- [X] T029 [US2] Create `src/AskLucy.Infrastructure/Buildings/BuildingRetrievalOptions.cs` (default radius 200 m, max count 300, cache TTL) bound via `IOptions<T>` with `ValidateOnStart`, and register the provider in `src/AskLucy.Infrastructure/DependencyInjection.cs` (FR-015)
- [X] T030 [US2] Add caching to the building lookup using the already-registered `IMemoryCache` (see `DependencyInjection.cs:192` and the `KnowledgeBaseDashboardSummaryCache` precedent), keyed by rounded latitude/longitude plus radius with a stated TTL. **This is required, not an optimisation**: the spec's own Clarification justifies routing building data through the platform partly on caching, and without it every activate/deactivate cycle re-queries the flakiest external dependency in the system (§15 — caching where staleness is acceptable and documented)
- [X] T031 [P] [US2] Create `src/AskLucy.Application/Buildings/Queries/GetSiteBuildings/` — query, handler and FluentValidation validator (latitude −90…90, longitude −180…180, radius 50…1000)
- [X] T032 [US2] Create `src/AskLucy.Web/Controllers/v1/SiteBuildingsController.cs` — `GET /api/v1/site-buildings`, `[ApiController]`, `[Authorize]`, dispatching via MediatR, mapping `BuildingProviderUnavailableException` to a `503` Problem Details (§6, FR-009, FR-045)
- [X] T033 [US2] Add a `buildings-endpoints` rate-limit policy in `src/AskLucy.Web/Program.cs` (alongside the existing `ai-endpoints`/`admin-endpoints`/`weather-endpoints` policies) and apply `[EnableRateLimiting("buildings-endpoints")]` to the controller. Constitution §6 requires every public endpoint be rate-limited; `WeatherController` is the exact precedent to copy. This matters beyond compliance — the endpoint proxies a shared free Overpass service that has its own limits and has already returned 429 to this system
- [X] T034 [P] [US2] Write `tests/AskLucy.Infrastructure.Tests/Buildings/OverpassBuildingFootprintProviderTests.cs` — tag parsing and provenance, the 9 m default, exclusion counting, the count cap setting `Limited`, the site-building rule, cache hit/miss behaviour, and an unavailable Overpass raising the typed exception
- [X] T035 [P] [US2] Write `tests/AskLucy.Application.Tests/Buildings/GetSiteBuildingsQueryHandlerTests.cs` and validator tests with a faked provider — no network (§10)

### Frontend — shadows and geometry

- [X] T036 [P] [US2] Implement `src/AskLucy.Web/ClientApp/src/features/solar/api/siteBuildingsApi.ts` — typed `apiFetch` call to `/site-buildings`, following the existing `documentsApi.ts` convention
- [X] T037 [US2] Implement `src/AskLucy.Web/ClientApp/src/features/solar/scene/sunLight.ts` — `THREE.DirectionalLight(0xfff2d0, 2.6)` plus `AmbientLight(0.7)` inside the extension's group, `castShadow`, orthographic shadow camera at `±(radius × 1.3)`, `mapSize 2048²`, `bias -0.0006`, `PCFSoftShadowMap`; `aimAt(solarPosition)` setting position from the ENU unit vector and `visible`/`intensity` to zero when the sun is below the horizon (FR-016, FR-017, research D16)
- [X] T038 [US2] Implement `src/AskLucy.Web/ClientApp/src/features/solar/scene/shadowGround.ts` — finite `PlaneGeometry(radius × 2.4)` with `THREE.ShadowMaterial({ opacity: 0.42 })`, `receiveShadow`, hidden when the sun is below the horizon (FR-016, FR-018)
- [X] T039 [US2] Write `sunLight.test.ts` and `shadowGround.test.ts` asserting the sun's ENU unit vector for known position/date/time, that the light direction is its negation, and — critically — that the ground plane's half-extent is **strictly less than** the shadow frustum half-extent (FR-018, research D16: the clamped-lookup grey-blob failure the reference implementation hit)
- [X] T040 [US2] Implement `src/AskLucy.Web/ClientApp/src/features/solar/buildings/footprintGeometry.ts` — ring lat/lng → specs/051 `worldToLocal` → `THREE.Shape` → `ExtrudeGeometry({ depth, bevelEnabled: false })`; material with `colorWrite: false`, `depthWrite: false`, `castShadow: true`, `receiveShadow: true`, and a `showMass` flag that re-enables `colorWrite` (FR-010 as amended, research D13, FR-041)
- [X] T041 [US2] Write `footprintGeometry.test.ts` asserting extrusion depth equals the resolved height, that meshes have `colorWrite === false` and `castShadow === true`, that the `showMass` toggle flips `colorWrite`, and that every vertex went through `worldToLocal` against the viewer's single reference point (FR-041)
- [X] T042 [US2] Fetch buildings on activate and on site change in `SolarAnalysisOverlay.tsx`, tagging each request with its site key and **discarding any response whose key no longer matches** (research D10 — the display must never mix one site's buildings with another's sun)
- [X] T043 [US2] Render the buildings notice — no buildings found, data unavailable, `limited`, and `excludedCount` — into the analysis's `partial` state using `copy.ts` wording, with the sun path continuing to work (FR-013, FR-014, FR-015)
- [X] T044 [US2] Declare `shadows` via `drawingSpace.declareDrawingRequirement('shadows')`, confirm it flips `renderer.shadowMap.enabled`, and perform a **before/after visual comparison** of the magma-glow and boundary-highlight layers, which were tuned without shadows (research D6 — this is its own reviewed step, not a side effect of adding a light). Record the outcome in the task's commit message (FR-038)
- [X] T045 [US2] Write `features/solar/scene/shadows.integration.test.ts` asserting no shadows are cast when the sun is below the horizon and that this is stated in the figures (FR-017, US2 scenario 3), **and that a taller building casts onto a shorter neighbour** — FR-016 requires shadows onto the ground *and onto each other*, which ground-only assertions would miss

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 — Watch Shadows Move Through the Day (Priority: P2)

**Goal**: Drag through the day or press play and watch shadows sweep across the site, with figures
tracking.

**Independent Test**: Drag through a full day and confirm shadows and figures update together and
stay consistent; play the day through and confirm it animates without degrading the viewer.

### Tests for User Story 3

- [X] T046 [P] [US3] Write `features/solar/panels/SolarTimeControlPanel.test.tsx` — slider and date changes update sun, shadows and figures **together** from the one `instantUtc` (FR-022); stopping playback leaves the moment where it stopped (FR-021); closing stops playback (FR-024); and the viewer remains interactive while playback runs (US3 scenario 4, FR-023)
- [X] T047 [P] [US3] Write a test asserting that when playback is **not** running the frame callback requests **zero** redraws (research D9 — this guard is what keeps the viewer quiet and is the substance of FR-039)

### Implementation for User Story 3

- [X] T048 [US3] Implement `src/AskLucy.Web/ClientApp/src/features/solar/panels/SolarTimeControlPanel.tsx` — date picker, 0…1439 local-minute slider, play/stop, speed; native `range` input with `aria-valuetext` carrying the local time so assistive technology reads "14:00", not "840" (FR-020, FR-021, FR-032)
- [X] T049 [US3] Register it as a live panel kind via `context.registerLivePanelKind({ typeKey: 'solar.time-control', ... })` in `solarAnalysisExtension.tsx`, with its zod schema and chrome (FR-030)
- [X] T050 [US3] Implement playback in `solarAnalysisStore.ts` plus the guarded `drawingSpace.onFrame` callback in `SolarScene.ts` — returns immediately and requests no redraw when not playing; advances the clock and calls `invalidate()` only while playing (FR-039, research D9)
- [X] T051 [US3] Ensure time changes move only the light direction and sun marker — building geometry is rebuilt only when buildings or heights change, which is what makes scrubbing cheap enough to read as continuous motion (FR-019, FR-022, FR-023, SC-004)
- [X] T052 [US3] Stop playback and withdraw everything on deactivate (FR-024, US3 scenario 5)

**Checkpoint**: Time scrubbing and playback work on top of US1 and US2.

---

## Phase 6: User Story 4 — Correct the Building Data (Priority: P2)

**Goal**: Correct a wrong or missing building height and adjust the analysis's ground offset, with
shadows updating to match.

**Independent Test**: Change a building's height and confirm its shadow changes correspondingly;
adjust the ground offset and confirm the analysis moves with it.

### Tests for User Story 4

- [X] T053 [P] [US4] Write `features/solar/store/correctionsStore.test.ts` — corrections are keyed by site and do not leak across sites (FR-028); invalid height (≤ 0 or > 1000 m) and invalid offset (outside ±500 m) are rejected with a stated reason from `copy.ts` and the **previous value kept**, never silently clamped (FR-027)

### Implementation for User Story 4

- [X] T054 [P] [US4] Implement `src/AskLucy.Web/ClientApp/src/features/solar/store/correctionsStore.ts` — `siteKey → { buildingHeights, groundOffsetMetres }`, session-scoped and deliberately not persisted (research D11, spec Out of Scope)
- [X] T055 [US4] Implement `src/AskLucy.Web/ClientApp/src/features/solar/panels/BuildingCorrectionsPanel.tsx` — shows the site building's height **and** whether it was `known` or `assumed` (FR-011), height correction (FR-025), ground offset (FR-026), the site label these corrections apply to (FR-028), and a reset; register it as live panel kind `solar.corrections` (FR-030)
- [X] T056 [US4] Apply a height correction by rebuilding only that building's geometry and its shadow (FR-019, FR-025)
- [X] T057 [US4] Apply the ground offset via `SolarScene.setGroundOffset(metres)` → `group.position.z`, moving dome, buildings, ground plane and light target together — **never** by re-anchoring the scene (FR-026, FR-041, research D15)
- [X] T058 [US4] Write `BuildingCorrectionsPanel.test.tsx` covering US4 scenarios 1–5, including that the provenance label is visible and that moving to a different site makes the correction's scope clear

**Checkpoint**: Corrections work; the analysis is trustworthy when source data is wrong.

---

## Phase 7: User Story 5 — Ask Lucy About the Sun (Priority: P3)

**Goal**: Lucy opens solar analysis for the active site and explains what it shows.

**Independent Test**: Ask Lucy about sunlight or shadows on the active site; confirm she opens the
analysis and describes what it shows.

### Tests for User Story 5

- [X] T059 [P] [US5] Write `tests/AskLucy.Application.Tests/Conversations/Capabilities/OpenSolarAnalysisCapabilityTests.cs` mirroring `LoadViewerContentCapabilityTests`' structure — schema validation, success echo, `no-active-site` refusal (FR-035), `viewer-unavailable` refusal (FR-036). Note: `AgentToolResult`'s property is `Output`, not `OutputJson`, and the test file needs `using AskLucy.Application.Abstractions;`

### Implementation for User Story 5

- [X] T060 [P] [US5] Create `src/AskLucy.Application/Viewer/SolarAnalysisCommand.cs` — `record SolarAnalysisCommand(string Date, string TimeOfDay)`
- [X] T061 [US5] Create `src/AskLucy.Application/Conversations/Capabilities/OpenSolarAnalysisCapability.cs` — `CapabilityKey = "open_solar_analysis"`, `IsOfferable => false`, optional `date`/`timeOfDay` and **no coordinates** (contracts/open-solar-analysis-capability.md — it acts on the active site by design); performs no solar computation (research D3, FR-033)
- [X] T062 [US5] Write the capability's description/instruction text so Lucy **describes what the analysis shows rather than reciting the figures the panel already displays** — FR-034, and the assumption research D3 relies on when it keeps the solar computation out of the backend entirely. Assert the wording in `OpenSolarAnalysisCapabilityTests` so this cannot silently regress into a figure-reciting prompt (FR-033, FR-034)
- [X] T063 [US5] Register the capability in `src/AskLucy.Application/DependencyInjection.cs` as both `OpenSolarAnalysisCapability` and an `IAgentTool`, following the `LoadViewerContentCapability` registration exactly
- [X] T064 [US5] Add `SolarAnalysisCommand? SolarAnalysis = null` to `src/AskLucy.Application/Ai/Commands/SendChatMessage/ChatStreamChunk.cs` and a matching `case` in `src/AskLucy.Application/Conversations/Runtime/StructuredPayloadExtractor.cs`
- [X] T065 [US5] Emit the trailing SSE event `data: __SOLAR_ANALYSIS__{json}` in `src/AskLucy.Web/Controllers/v1/AiController.cs`, mirroring the existing `__ZOOM__`/`__VIEWER_CONTENT__` blocks. **Keep `using` directives in alphabetical order** — an out-of-order import failed CI on the specs/051 commit
- [X] T066 [US5] Add the `solarAnalysis` member to `ChatStreamEvent` and its parse block in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts`
- [X] T067 [US5] Handle the event in `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts` — activate `viewer.solar-analysis` with the supplied date/time (both call sites, as specs/051 required)
- [X] T068 [US5] Write `aiApi.test.ts`/`useChatStream.test.ts` coverage for the new event, asserting no activation occurs on a refusal payload (FR-035, FR-036)

**Checkpoint**: All five user stories are independently functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T069 [P] Write the fifty-cycle teardown test in `features/solar/solarAnalysisExtension.test.ts` — after 50 activate/deactivate cycles, scene child count, `renderer.shadowMap.enabled`, panels, toolbar entries and event subscriptions all return to baseline, with no undisposed geometry or materials (SC-006, SC-007, FR-040), mirroring specs/051's own fifty-cycle assertion
- [X] T070 [P] Write jest-axe accessibility tests for all three panels — time control, corrections and the figures content panel (FR-032, §7 WCAG 2.1 AA, §10)
- [X] T071 [P] Write the failure-surface test sweep asserting every row of quickstart Scenario 7 produces a user-visible explanation and that **zero** failures are observable only in logs (SC-008, FR-045, FR-046, constitution §2.VIII)
- [X] T072 [P] Document the feature in `src/AskLucy.Web/ClientApp/src/features/solar/README.md` — the modules, the ported NOAA provenance and its stated tolerances, and the five constraints carried from the reference implementation (research D13–D17) so they are not rediscovered
- [X] T073 **SC-009 check**: run `git diff --name-only main -- src/AskLucy.Web/ClientApp/src/viewer/` and confirm only `extensions/builtin/solarAnalysisExtension.tsx` and `extensions/declared.ts` appear. Any other **code** file is an SC-009 finding belonging to specs/049–051 — record it in this spec's quickstart rather than absorbing it here. `viewer/README.md` is documentation, not a framework change, and is exempt (§13 still expects docs to track the code)
- [X] T074 Run the full verification sweep: `npx tsc -b --noEmit`, `npm test`, `npx eslint .` (the React Compiler's `react-hooks/set-state-in-effect` rule is an **error**, not a warning — it caught a real bug in specs/051), `dotnet build "Ask Lucy.sln"`, `dotnet test "Ask Lucy.sln" --no-build`
- [X] T075 Walk quickstart.md scenarios 1–11, recording the outcome of each. Three have parts that cannot be measured in this environment and must be marked **not measured** with the stated method for a human, as specs/050 and specs/051 both did, rather than claiming a pass never observed: Scenario 2 (rendered shadow pixels), Scenario 4 (frame rate, SC-004) and Scenario 10 (time-to-understanding, **SC-003** — a usability-timing criterion needing a person at a browser)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: depends on Setup — **blocks every user story**
- **US1 (Phase 3)** and **US2 (Phase 4)**: both depend only on Foundational; independent of each other
- **US3 (Phase 5)**: depends on Foundational; meaningfully demonstrable only once US1 (sun) and US2 (shadows) exist
- **US4 (Phase 6)**: depends on US2 (there must be a building to correct)
- **US5 (Phase 7)**: depends on the extension existing (Foundational); backend work is fully parallel to all frontend work
- **Polish (Phase 8)**: depends on all desired stories

### Within Each User Story

- Tests before or alongside implementation; never deferred to a follow-up (§10)
- Backend provider → caching → query handler → controller → rate-limit policy
- Geometry/store → panel → wiring into the extension

### Parallel Opportunities

- T002, T003 in Setup
- T004, T005, T007, T009 in Foundational (four independent modules)
- **US1 and US2 can be built by different people simultaneously** after Phase 2
- The entire US5 backend chain (T059–T065) is independent of all frontend work
- T069–T072 in Polish

---

## Parallel Example: Foundational Phase

```bash
Task: "Port NOAA solar-position algorithm in features/solar/solar/solarPosition.ts"     # T004
Task: "Implement daySummary with polar-condition handling in solar/daySummary.ts"        # T005
Task: "Implement time-zone resolution in features/solar/solar/timeZone.ts"               # T007
Task: "Create centralized user-facing copy in features/solar/copy.ts"                    # T009
```

## Parallel Example: User Story 2 backend

```bash
Task: "Create BuildingFootprint + BuildingFootprintResult in Application/Buildings/"      # T024
Task: "Create IBuildingFootprintProvider + typed exception in Application/Buildings/"     # T025
Task: "Create GetSiteBuildings query, handler and validator"                             # T031
```

---

## Implementation Strategy

### MVP scope — Phases 1, 2 and 3 (US1)

Sun path and figures, working on any site with no building data at all. This is genuinely useful on
its own, answers a real question, and proves the framework carries a real capability before any of
the shadow machinery exists. Phase 2 now contains no shadow-specific work, so the MVP really is
just these three phases.

1. Phase 1 Setup
2. Phase 2 Foundational
3. Phase 3 US1 → **stop and validate** against quickstart Scenario 1
4. Demo

### Incremental delivery

US1 (sun path) → US2 (shadows) → US3 (time) → US4 (corrections) → US5 (Lucy). Each adds value
without breaking what came before.

---

## Notes

- **Three constraints are binding and easy to lose**: buildings cast but are not drawn (T040);
  the ground plane must sit strictly inside the shadow frustum (T039); the ground offset moves the
  extension's own group, never the scene anchor (T057). Each was discovered the hard way by the
  reference implementation.
- **Two live SC-009 risks** are recorded in research D9 (`onFrame` has no unsubscribe) and D17
  (no `environment` drawing requirement). If either forces a framework change, it belongs to
  specs/051 and must be recorded as a finding, not absorbed here.
- Commit after each task or logical group; the constitution requires tests to travel with the
  behaviour they cover, so a task is not done when its test is missing.
