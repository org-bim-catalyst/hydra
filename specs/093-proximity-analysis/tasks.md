# tasks.md - Proximity Analysis

**Feature**: Proximity Analysis (`specs/093-proximity-analysis`)

**Revised**: 2026-11-06 — aligned to SPEC-094's Spatial Request Bundle & Analysis Orchestrator

**Integration**: SPEC-094 already declares `proximity.analysis`, interprets
`category`/`targetCategory`/`distance`/`unit`/`beyond`/`area` into a `SpatialOperation`, resolves
the study area, validates thresholds, merges results and delivers `__SPATIAL_LAYERS__`. 093
therefore adds a **specialist**, not a capability or a viewer layer system.

## Phase 1: Integration (complete)

- [X] T001 Confirm the specialist seam: `ISpatialSpecialist`, `SpatialSpecialistRegistry`, `SpatialAnalysisCoordinator`, `StudyAreaResolver`, `SpatialResultNormalizer`, `SpatialLayerStateStore`; record in `research.md`.
- [X] T002 Confirm `SpatialOperationKinds.ProximityAnalysis` is declared and interpreted by `SpatialAnalysisCoordinator.BuildOperation`; no new capability or endpoint is required.
- [X] T003 Reuse `GeometryMath.DistanceMeters` / `CirclePolygon`; no new geometry dependency.

## Phase 2: Specialist foundation (complete)

- [X] T004 Add `src/AskLucy.Application/Spatial/Specialists/ProximityAnalysisSpecialist.cs` implementing `ISpatialSpecialist` for `proximity.analysis`.
- [X] T005 Register it in `src/AskLucy.Application/DependencyInjection.cs` beside `PoiDiscoverySpecialist`.
- [X] T006 Retrieve source and target feature sets concurrently through `IPoiQueryProvider` inside the resolved study area; report `DataSourceCoverage`; treat provider failure as `Failed`, never `Empty`.

## Phase 3: User Story 1 — within-distance (P1) (complete)

- [X] T007 Inclusive `within` semantics (distance ≤ threshold), in metres after unit conversion.
- [X] T008 Matching-feature marker layer with the source category's catalogue style.
- [X] T009 Buffer layers around targets for `within` only, capped at 10 with a warning when exceeded.
- [X] T010 Unit tests: within selection, buffer styling, provider outage vs empty, unsupported category, missing target/area.

## Phase 4: User Story 2 — beyond-distance (P1) (complete, point-based)

- [X] T011 Exclusive `beyond` semantics (distance > threshold).
- [X] T012 No buffer layers for `beyond` — a ring would invert the meaning of the result.
- [X] T013 Empty target set reports `Empty` with a warning naming the target category.
- [X] T014 Unit test: beyond excludes everything at or inside the threshold.
- [ ] T015 **Deferred decision**: area-based “residential areas beyond …” needs polygon features; `IPoiQueryProvider` returns points. v1 documents the point-based limitation; a polygon provider is a separate change.

## Phase 5: User Story 3 — distance bands (P2) (implemented, verification pending)

- [X] T016 Specialist assigns each feature to exactly one band: lower-inclusive, upper-exclusive, final upper bound inclusive.
- [X] T017 One marker layer per non-empty band, distinct colour; no buffers for banded results.
- [X] T018 Accept a `bands` array through `SpatialAnalysisCapability`'s operation schema and `SpatialOperationInput`; the coordinator maps it to `SpatialDistanceThreshold`s.
- [X] T019 Unit test: adjacent 0–500 m / 500 m–1.5 km bands never both claim a feature; features beyond the final bound belong to no band.
- [X] T020 Coordinator test: a `bands` operation reaches the specialist with ordered thresholds and no within/beyond mode.
- [X] T021 Frontend verification: band layers use the existing per-layer style/colour path in `SpatialLayersOverlay`; store/legend tests pass.

## Phase 6: Polish

- [X] T022 Update `quickstart.md` (implementation path, point-based beyond limitation, missing-area and provider-failure expectations) and the contract's implementation-alignment note.
- [X] T023 Full Application suite (3007 passing), Infrastructure suite (992 passing, 4 skipped), Web C# compile, frontend typecheck plus spatial store/legend/chat tests (39 passing).
- [ ] T024 SC-003 check in a live environment. A direct measurement attempt on 2026-11-06 hit the public Overpass endpoint at capacity (502 "all query workers busy"), so the figure remains unrecorded; record it once the endpoint is healthy and the feature is deployed.
- [X] T025 Update `contracts/proximity-analysis-api.md` to state that proximity is a specialist of the 094 bundle (no separate REST route).
- [X] T026 Harden area matching: when an exact administrative-area name returns nothing and the area's extent is city-scale (≤ 200 km diagonal), retry with the bounding box; country-scale extents keep area filtering so neighbours are never included.
- [X] T027 Fix result count semantics: buffer layers are visualization, not results, so one within-match no longer narrates as "2 results".

## MVP

US1 (T001–T010) is shippable on its own: “schools within 1 km of hospitals” now returns markers and
buffers through the existing spatial capability, with no new endpoint or viewer path.
