# Tasks: Solar Analysis Accuracy & Performance

**Input**: Design documents from `/specs/063-solar-accuracy-performance/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/](./contracts/), [baseline.md](./baseline.md)

**Tests**: Included and required. This release's entire correctness claim is a number that must be
asserted (−0.267°), and every performance item carries a stated invariant that a test is the only
practical guard for. The constitution makes unit-testability non-negotiable (§2 V).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel — different files, no dependency on an incomplete task
- **[Story]**: US1–US4 from spec.md
- All paths are relative to the repository root

## Path Conventions

Frontend-only change. All source paths are under
`src/AskLucy.Web/ClientApp/src/features/solar/` unless stated otherwise. Tests live beside the
modules they cover, per the existing convention in that folder.

---

## Phase 1: Setup

**Purpose**: None required.

No new dependency, no new folder, no configuration change. `BufferGeometryUtils` is already
transitively present via Three.js. Phase 1 is intentionally empty; proceeding to Phase 2.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish the corrected altitude that US1 asserts, US2 renders from, and US3 and US4
sample. Nothing else can proceed until the single-altitude rule of FR-007 is in place, because
every other story would otherwise be built against a value that is about to change.

**⚠️ CRITICAL**: No user story phase may begin until T005 passes.

- [ ] T001 [P] Create `solar/refraction.ts` with `refractionCorrectionDegrees()` implementing the NOAA piecewise correction per [contracts/solar-position.md](./contracts/solar-position.md), and export `SOLAR_SEMIDIAMETER_DEGREES = 0.2667` with a TSDoc note recording that the ±1.7% annual variation is deliberately not modelled
- [ ] T002 [P] Create `solar/refraction.test.ts` asserting: zero above 85°, continuity across each band boundary within tolerance, defined output below the horizon, and the published NOAA correction values at 0°, 5°, 15° and 45°
- [ ] T003 In `solar/solarPosition.ts`, apply `refractionCorrectionDegrees()` so `altitudeDegrees` is the corrected apparent altitude, keeping the geometric value as a local intermediate that is not returned or exported (FR-007)
- [ ] T004 In `solar/solarPosition.test.ts`, assert azimuth is bit-unchanged and altitude above 15° is within tolerance of the pre-change values recorded in [baseline.md](./baseline.md) (FR-005, US1 scenario 3)
- [ ] T005 In `solar/daySummary.ts`, replace the `Math.sin(toRad(-0.833))` threshold with a Newton solve for `solarPosition().altitudeDegrees + SOLAR_SEMIDIAMETER_DEGREES = 0`, seeded from the existing closed form, with a stated iteration cap and convergence threshold (FR-002a)
- [ ] T006 In `solar/daySummary.ts`, evaluate the existing `cosH0 > 1` / `cosH0 < -1` polar branches **before** any iteration, and return the existing never-rises / never-sets results unchanged (FR-004)
- [ ] T007 In `solar/daySummary.ts`, surface solver non-convergence to the caller as an explicit undetermined result rather than returning the seed or the last iterate, and in `panels/solarFiguresContent.ts` render it as a stated "could not be determined" alongside the existing polar wording (FR-028, constitution §2 VIII)
- [ ] T008 Add `solar/daySummary.test.ts` coverage for T007's non-convergence path by injecting a forced failure, asserting the undetermined result reaches the panel as visible text and is never a plausible-looking time

**Checkpoint**: The corrected altitude is the system's only altitude, and rise/set is solved rather than approximated.

---

## Phase 3: User Story 1 — The Numbers Agree With Each Other (Priority: P1) 🎯 MVP

**Goal**: The figures panel stops contradicting itself. At the sunrise it reports, the altitude reads
−0.27° — the sun's angular radius — at every site and every date.

**Independent Test**: Set the time to the feature's own reported sunrise at several sites and dates;
the altitude reads −0.27° every time, and the below-horizon message is gone.

### Tests for User Story 1

- [ ] T009 [P] [US1] In `solar/daySummary.test.ts`, add the feature's central assertion: at both returned rise and set instants, `solarPosition().altitudeDegrees` equals `-SOLAR_SEMIDIAMETER_DEGREES` within `SOLAR_POSITION_TOLERANCE_DEGREES`, across Dubai, London, Singapore, Tromsø and Reykjavík × March, June, September and December (SC-001)
- [ ] T010 [P] [US1] In the same file, assert the value is **identical** across all of those site/date pairs, so a site-dependent residual fails even if each individual value is within tolerance (FR-002)
- [ ] T011 [P] [US1] In `solar/daySummary.test.ts`, assert rise/set against published NOAA values within the existing 60 s / 600 s tolerances, with a comment recording that the current release's output is explicitly **not** the reference (FR-004a, SC-003)
- [ ] T012 [P] [US1] In `solar/solarPosition.test.ts`, assert full-day altitude against NOAA's published **corrected-for-refraction** column at every test location (SC-002)
- [ ] T013 [P] [US1] In `solar/daySummary.test.ts`, assert the polar never-rises and never-sets cases are unchanged and are decided by the same upper-edge definition as rise and set (FR-004, US1 scenario 6)

### Implementation for User Story 1

- [ ] T014 [US1] Measure the corrected altitude's maximum deviation from NOAA's published corrected column across the test locations, and set `SOLAR_POSITION_TOLERANCE_DEGREES` in `solar/solarPosition.ts` to the measured value rather than the inherited `0.1` (FR-006, research D10)
- [ ] T015 [US1] In `copy.ts` and `panels/solarFiguresContent.ts`, state which altitude quantity the figure shows, so a user comparing against an external reference knows which of its two published columns to use (FR-003, US1 scenario 5)
- [ ] T016 [US1] In `panels/solarFiguresContent.test.ts`, assert the quantity label is present and that the tolerance wording reads from the exported constant rather than a literal, so T014's re-measurement propagates automatically
- [ ] T017 [US1] Verify the below-horizon message no longer appears at the reported sunrise, and add a regression assertion pinning that — the exact contradiction captured in [baseline.md](./baseline.md)

**Checkpoint**: US1 is independently shippable. The correctness defect is closed.

---

## Phase 4: User Story 2 — Shadows Stay Sharp and Complete (Priority: P1)

**Goal**: Shadow detail is spent on ground that has buildings on it, and long low-sun shadows are
drawn to their full length instead of stopping at an invisible boundary.

**Independent Test**: At a low sun angle, shadow edges are equally or more defined than baseline and
no shadow is truncated; no grey patch appears at any sun position.

**⚠️ Carries the release's main technical risk** — the specs/052 constraint-4 grey-blob failure.

### Tests for User Story 2

- [ ] T018 [P] [US2] In `scene/shadowGround.test.ts`, extend the existing ground-inside-frustum invariant to content-derived radii: a tight cluster, a lone tall building, an off-centre cluster, and the no-buildings fallback (FR-009, FR-010, SC-007)
- [ ] T019 [P] [US2] In `scene/sunLight.test.ts`, assert the derived radius accommodates the longest shadow the tallest present building casts at `MIN_SHADOW_ELEVATION_DEGREES`, so tighter fitting cannot truncate it (FR-014, SC-006)
- [ ] T020 [P] [US2] In `scene/sunLight.test.ts`, assert the light is disabled at and below 1° exactly as it already is below the horizon, and that the radius never diverges as elevation approaches zero (FR-012)
- [ ] T021 [P] [US2] In `scene/shadows.integration.test.ts`, assert shadow direction at a fixed instant is unchanged from baseline above 15° elevation and differs by no more than the refraction correction at the horizon (FR-027, SC-009)

### Implementation for User Story 2

- [ ] T022 [US2] In `scene/sunLight.ts`, remove `SHADOW_FRUSTUM_RATIO` and add `MIN_SHADOW_ELEVATION_DEGREES = 1.0` with a TSDoc note recording the divergence reasoning from research D7
- [ ] T023 [US2] In `scene/sunLight.ts`, derive the shadow radius as `buildingExtent + tallestHeight / tan(MIN_SHADOW_ELEVATION_DEGREES)`, keeping it a **single scalar** that feeds both the shadow camera extent and `scene/shadowGround.ts` (FR-008, FR-009)
- [ ] T024 [US2] In `scene/sunLight.ts`, apply the defined fallback radius when no buildings are present, and keep `near`/`far` bracketing the tallest building as today (FR-013)
- [ ] T025 [US2] In `scene/sunLight.ts`, disable the directional light at or below 1° elevation, using the same path that already handles below-horizon (FR-012)
- [ ] T026 [US2] In `copy.ts` and `panels/solarFiguresContent.ts`, state why no shadows are drawn in the new 1°-to-horizon window, so the sun being visibly up with no shadows reads as intended rather than broken (FR-012, FR-028)
- [ ] T027 [US2] Confirm no code path added here writes `renderer.shadowMap.*`, `scene.environment`, or calls `sceneAnchor.set(...)` (FR-024, README constraints 2 and 3)

**Checkpoint**: US1 + US2 deliverable together — the two P1 stories, and everything user-visible in the release.

---

## Phase 5: User Story 3 — Scrubbing Stays Smooth on Dense Sites (Priority: P2)

**Goal**: 300 buildings scrub and play continuously, because the drawing cost stops scaling with
building count and shadows stop recomputing when the sun has barely moved.

**Independent Test**: Drag across a full day at the 300-building cap and play it back while rotating
the camera; motion stays continuous and the viewer stays usable.

### Tests for User Story 3

- [ ] T028 [P] [US3] In `buildings/footprintGeometry.test.ts`, assert N buildings produce exactly one geometry and one material, that the mass toggle still reveals **all** of them, and that a degenerate ring is excluded without failing the merge (FR-015, FR-016)
- [ ] T029 [P] [US3] In `buildings/footprintGeometry.test.ts`, assert an empty building list produces no mesh rather than an empty-geometry mesh, and that `castShadow` / `colorWrite: false` / `depthWrite: false` semantics are unchanged (FR-017)
- [ ] T030 [P] [US3] In `solarAnalysisExtension.test.ts`, assert the playback gate skips recomputation below 0.25° of sun movement, and that a height correction, a ground-offset change and newly arrived building data each clear it immediately (FR-018, FR-019)

### Implementation for User Story 3

- [ ] T031 [US3] In `buildings/footprintGeometry.ts`, merge all footprint extrusions into one `BufferGeometry` with one shared material via `BufferGeometryUtils.mergeGeometries` (FR-015)
- [ ] T032 [US3] In `buildings/footprintGeometry.ts`, route excluded degenerate rings through the existing excluded-count surface rather than dropping them silently (FR-028)
- [ ] T033 [US3] In `buildings/footprintGeometry.ts`, expose the merged extent and tallest height so T023's radius derivation does not re-traverse the source data
- [ ] T034 [US3] In `buildings/footprintGeometry.ts`, reimplement the mass toggle as a `colorWrite` flip on the single shared material (FR-016)
- [ ] T035 [US3] Add `SHADOW_GATE_DEGREES = 0.25` and apply the gate in `scene/SolarScene.ts` and `viewer/extensions/builtin/solarAnalysisExtension.tsx` by withholding the recomputation inputs — **never** by touching `renderer.shadowMap.autoUpdate` or any renderer-global flag (FR-018, FR-024)
- [ ] T036 [US3] Restrict the gate to continuous playback only; scrubbing and single-step time changes are never gated (FR-020)
- [ ] T037 [US3] If the gate cannot be achieved within FR-024, stop and record a `DrawingRequirement` against specs/051 as an SC-009 finding — do **not** reach into renderer state, and ship without the gate (research D8)

**Checkpoint**: Dense sites scrub smoothly. The release is complete except for the dome split.

---

## Phase 6: User Story 4 — Changing the Date Does Not Rebuild the Sky (Priority: P3)

**Goal**: Stepping dates updates the day's arc only. The dial, mount and monthly lattice stay put.

**Independent Test**: Step through a sequence of dates; the arc updates each time and the fixed
furniture never flickers, moves or changes.

### Tests for User Story 4

- [ ] T038 [P] [US4] In `scene/sunPathCurve.test.ts`, assert a date change rebuilds only the dated group and leaves the fixed group's object identities untouched (FR-021, SC-008)
- [ ] T039 [P] [US4] In `scene/sunPathCurve.test.ts`, assert a site change and a year change both rebuild the fixed group, and that `buildDomeShell` remains built but **not assembled into the scene** (FR-022, README constraint 5)
- [ ] T040 [P] [US4] In `scene/sunPathCurve.test.ts`, assert arcs remain `TubeGeometry`/`CylinderGeometry` and no `THREE.Line` is introduced (README constraint 5)
- [ ] T041 [P] [US4] In `scene/SolarScene.ts`'s test coverage, assert repeated date changes, site changes and open/close cycles leave nothing accumulated in either disposal scope (FR-023, SC-010)

### Implementation for User Story 4

- [ ] T042 [US4] In `scene/sunPathCurve.ts`, split `buildSunPath` into a fixed-furniture builder (dial, mount post, monthly lattice) and a dated-path builder (day arc, hour marks, seasonal extremes, current marker) per [contracts/solar-scene.md](./contracts/solar-scene.md)
- [ ] T043 [US4] In `scene/SolarScene.ts`, hold the two as sibling groups under the extension's Drawing Space group, each with its own disposal scope, reusing the existing deep `disposeGroupContents` rather than reimplementing it (FR-023)
- [ ] T044 [US4] In `scene/SolarScene.ts`, dispose and rebuild the dated group on date or instant change and the fixed group only on site change, year change or extension stop; ensure `disposeAll` covers both (FR-021, FR-022)
- [ ] T045 [US4] Confirm `setGroundOffset` still moves the extension's own group and does not call `sceneAnchor.set(...)` (README constraint 3)

**Checkpoint**: All four stories complete.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T046 [P] Update `features/solar/README.md`'s module map and constraint notes for the split builders, the merged geometry, the derived radius and the new 1° floor — the README records the binding constraints and is part of the deliverable, not documentation of it
- [ ] T047 [P] Update `specs/052-solar-analysis/spec.md` with a pointer recording which of its behaviours this feature supersedes (constitution: documentation is part of implementation)
- [ ] T048 Run the full frontend suite, not just the touched files — `npx vitest run` from `src/AskLucy.Web/ClientApp` — because page-level tests carry their own assertions about components they render
- [ ] T049 Run `npx tsc -b --noEmit` from `src/AskLucy.Web/ClientApp`; the bare `tsc --noEmit` is a silent no-op under this project's references
- [ ] T050 Re-verify every FR-026 preservation item: the five README constraints, unchanged basemap compositing, unchanged `/api/v1/site-buildings` usage
- [ ] T051 Produce the post-implementation verification instructions from [quickstart.md](./quickstart.md) Part 2 as numbered mechanical steps with expected values, for screenshot-based verification against [baseline.md](./baseline.md)

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 2 (Foundational)** — blocks everything. T005 is the gate.
- **Phase 3 (US1)** — depends on Phase 2 only.
- **Phase 4 (US2)** — depends on Phase 2 only. Independent of US1.
- **Phase 5 (US3)** — depends on Phase 2. **T023 (US2) depends on T033 (US3)** for the extent/height figures; do T033 before T023, or have T023 compute them locally and simplify when T033 lands.
- **Phase 6 (US4)** — depends on Phase 2 only. Independent of US1, US2 and US3.
- **Phase 7 (Polish)** — after all stories.

### Within each phase

Tests before implementation. Tasks marked [P] touch different files and may run together.

### The one cross-story coupling

US2's radius derivation wants the merged geometry's extent, which US3 produces. This is the only
place the stories are not independent. Resolve it by ordering T033 before T023.

---

## Parallel Execution Examples

**Phase 2 start**: T001 and T002 together (new file plus its test).

**Phase 3 tests**: T009, T010, T011, T012 and T013 are five assertions across two test files — all [P].

**Across stories**: Once Phase 2 is complete, US1, US2 and US4 can proceed concurrently; they share
no source file. US3 should be sequenced with US2 per the coupling above.

---

## Implementation Strategy

### MVP

**Phase 2 + Phase 3 (US1).** That alone closes the correctness defect — the panel stops
contradicting itself — and is independently shippable and verifiable against
[baseline.md](./baseline.md).

### Incremental delivery

1. Phase 2 → foundation in place, nothing user-visible yet
2. + US1 → **ship-worthy**; the defect is closed
3. + US2 → shadows sharper and complete; the release's remaining user-visible change
4. + US3 → dense sites scrub smoothly
5. + US4 → date stepping stops rebuilding the sky
6. Polish

### Verification

Per the standing agreement, verification after each shippable increment is screenshot-based: the
implementer supplies numbered mechanical steps and expected values, and judges the returned
screenshots. The user is never asked to assess whether a solar figure is correct.
