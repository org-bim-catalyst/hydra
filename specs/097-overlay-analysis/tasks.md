# Tasks: Overlay Analysis

**Input**: Design documents in `specs/097-overlay-analysis/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/overlay-analysis.contract.md`

**Tests**: Included because the project constitution requires tests for observable behavior and the overlay contract relies on correctness of geometry and criteria.

**Organization**: Tasks are grouped by user story. All tasks use existing projects and dependencies; no package or database migration is planned.

## Phase 1: Setup

**Purpose**: Confirm the existing spatial feature contracts and file locations before overlay changes.

- [x] T001 Review the current bundle, specialist, feature-query geometry, stream event, viewer store, and legend contracts against `specs/097-overlay-analysis/contracts/overlay-analysis.contract.md`

## Phase 2: Foundational Contracts and Validation

**Purpose**: Add the typed overlay request/result seams required by all user stories.

- [x] T002 [P] Add overlay operation kind, typed inputs, steps, criteria, scoring and optional result metadata in `src/AskLucy.Application/Spatial/SpatialRequestBundle.cs`
- [x] T003 [P] Define overlay dataset/provider/result/score contracts in `src/AskLucy.Application/Spatial/Overlay/OverlayContracts.cs`
- [x] T004 Add bundle validation for unique references, bounded area, valid operation dependencies, geometry compatibility metadata, thresholds and weights in `src/AskLucy.Application/Spatial/SpatialBundleValidator.cs`
- [x] T005 Add overlay operation and result normalization tests in `tests/AskLucy.Application.Tests/Spatial/Overlay/OverlayContractTests.cs`
- [x] T006 Extend normalized spatial feature metadata for overlay criteria, lineage and optional score/rank in `src/AskLucy.Application/Spatial/SpatialAnalysisResult.cs`
- [x] T007 Add strict client-side overlay result validation and backward-compatible layer parsing in `src/AskLucy.Web/ClientApp/src/store/spatialLayersStore.ts`
- [x] T008 Add client layer contract tests for overlay metadata, malformed geometries, independent visibility and removal in `src/AskLucy.Web/ClientApp/src/store/spatialLayersStore.test.ts`
- [x] T009 Register the overlay specialist/provider seam in `src/AskLucy.Application/DependencyInjection.cs` and `src/AskLucy.Infrastructure/DependencyInjection.cs`

**Checkpoint**: The shared request/result contracts validate and preserve existing spatial operation behavior before story-specific execution and UI work begins.

## Phase 3: User Story 1 - Find Locations Meeting Several Conditions (Priority: P1)

**Goal**: Interpret a natural-language site-selection request and return candidates meeting every hard condition.

**Independent Test**: With fixed parcel, population, road and facility sources, verify the resolved criteria and candidate set against independently calculated expected results.

### Tests for User Story 1

- [x] T010 [P] [US1] Add specialist tests for candidate inputs, bounded area, all-required criteria, empty result, partial coverage and visible failures in `tests/AskLucy.Application.Tests/Spatial/Overlay/OverlayAnalysisSpecialistTests.cs`
- [x] T011 [P] [US1] Add deterministic provider tests for attribute, area and distance criteria over fixed fixtures in `tests/AskLucy.Infrastructure.Tests/Spatial/Overlay/OverlayAnalysisProviderTests.cs`
- [x] T012 [P] [US1] Add capability schema and natural-language operation parsing tests for overlay criteria and focused clarification in `tests/AskLucy.Application.Tests/Conversations/Capabilities/SpatialAnalysisCapabilityTests.cs`

### Implementation for User Story 1

- [x] T013 Add `overlay.analysis` to the supported operation descriptions and argument schema in `src/AskLucy.Application/Spatial/SpatialRequestBundle.cs` and `src/AskLucy.Application/Conversations/Capabilities/SpatialAnalysisCapability.cs`
- [x] T014 Parse overlay input roles, criteria, bounded area and clarification state into typed bundle values in `src/AskLucy.Application/Conversations/Capabilities/SpatialAnalysisCapability.cs`
- [x] T015 Implement the registered `OverlayAnalysisSpecialist` to resolve catalogued datasets, apply required criteria and return distinct complete/partial/empty/failed outcomes in `src/AskLucy.Application/Spatial/Specialists/OverlayAnalysisSpecialist.cs`
- [x] T016 Implement catalog-backed attribute, area and distance criterion execution under the validated study area in `src/AskLucy.Infrastructure/Spatial/Overlay/OverlayAnalysisProvider.cs`
- [x] T017 Add operation-level structured audit logging for resolved inputs, criteria, coverage, outcome, duration and result counts in `src/AskLucy.Application/Spatial/Overlay/OverlayAnalysisAuditLogger.cs`

**Checkpoint**: A site-selection request returns only eligible features, or one visible clarification/failure, through the established coordinator path.

## Phase 4: User Story 2 - Combine and Compare Spatial Layers (Priority: P1)

**Goal**: Support geometric intersection, union, directional difference and spatial-intersection selection.

**Independent Test**: Apply each operation to fixed point, line and polygon fixtures and compare exact output geometry and provenance.

### Tests for User Story 2

- [x] T018 [P] [US2] Add expected-geometry tests for intersection, union, difference direction, multipart output and spatial selection in `tests/AskLucy.Infrastructure.Tests/Spatial/Overlay/OverlayGeometryOperationTests.cs`
- [x] T019 [P] [US2] Add tests rejecting invalid rings, unresolved references and cyclic step dependencies; source coordinate scope is WGS84 because the current provider contract has no CRS metadata in `tests/AskLucy.Application.Tests/Spatial/Overlay/OverlayValidationTests.cs`

### Implementation for User Story 2

- [x] T020 Implement NetTopologySuite conversions and bounded intersection, union and directional difference operations in `src/AskLucy.Infrastructure/Spatial/Overlay/OverlayGeometryEngine.cs`
- [x] T021 Implement spatial-intersection feature selection while preserving original feature geometry and stable source identity in `src/AskLucy.Infrastructure/Spatial/Overlay/OverlayGeometryEngine.cs`
- [x] T022 Execute dependency-ordered steps, preserve step counts and source lineage, and reject unsupported coordinate transforms in `src/AskLucy.Infrastructure/Spatial/Overlay/OverlayAnalysisProvider.cs`
- [x] T023 Map overlay step outputs into normalized renderable feature geometries without changing existing feature-query semantics in `src/AskLucy.Application/Spatial/SpatialResultNormalizer.cs`

**Checkpoint**: Every supported operation produces correct, labeled geometry and never confuses selection with geometry construction.

## Phase 5: User Story 3 - Rank Candidates Using Weighted Criteria (Priority: P2)

**Goal**: Score only eligible candidates and explain reproducible ranks.

**Independent Test**: Score a fixed candidate set with stated weights and compare each normalized criterion, aggregate score and stable rank to an independent calculation.

### Tests for User Story 3

- [x] T024 [P] [US3] Add tests for score normalization, direction, weight validation, equal-weight defaults, tie-breaking and hard-constraint exclusion in `tests/AskLucy.Application.Tests/Spatial/Overlay/OverlayScoringTests.cs`

### Implementation for User Story 3

- [x] T025 Implement criterion normalization, direction, effective weights, aggregate score and deterministic ranking in `src/AskLucy.Application/Spatial/Overlay/OverlayScorer.cs`
- [x] T026 Return per-criterion values, effective weights, score and rank in overlay result features in `src/AskLucy.Application/Spatial/Specialists/OverlayAnalysisSpecialist.cs`
- [x] T027 Include score explanation and disclosed defaults in the user-facing spatial result summary in `src/AskLucy.Application/Conversations/Capabilities/SpatialAnalysisCapability.cs`

**Checkpoint**: Scores reproduce from disclosed inputs, and ineligible candidates never enter the ranked set.

## Phase 6: User Story 4 - Inspect and Manage Overlay Layers (Priority: P2)

**Goal**: Display, inspect, toggle and remove overlay source/result layers in 2D and 3D.

**Independent Test**: Render polygon, line and point results, inspect an item, toggle/remove a single layer, switch map modes and confirm independent state and correct legends.

### Tests for User Story 4

- [x] T028 [P] [US4] Add 2D overlay result rendering and feature-selection tests for polygon, line and point geometries in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayersOverlay.test.tsx`
- [x] T029 [P] [US4] Add accessible legend assertions for input/result role, operation, criteria, coverage and score meaning in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayerLegend.test.tsx`
- [x] T030 [P] [US4] Add result inspection assertions for eligibility, provenance, score and rank in `src/AskLucy.Web/ClientApp/src/features/viewer/components/FeatureAttributePanel.test.tsx`

### Implementation for User Story 4

- [x] T031 Render overlay source and result geometries through the existing 2D map overlay, with safe styles and visible render errors in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayersOverlay.tsx`
- [x] T032 Display operation, criteria, status, coverage and scoring metadata in the accessible legend in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayerLegend.tsx`
- [x] T033 Display feature eligibility, provenance, criterion scores and rank in the existing inspection panel in `src/AskLucy.Web/ClientApp/src/features/viewer/components/FeatureAttributePanel.tsx`
- [ ] T034 Confirm rendered 3D view consumes normalized features and add regression coverage. Shared layer-store state across viewer modes is covered in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.test.tsx`; live 3D rendering remains unverified.

**Checkpoint**: Layer operations affect only their own overlay; renderer, legend and inspection remain synchronized in 2D/3D.

## Phase 7: User Story 5 - Understand Limits and Failures (Priority: P2)

**Goal**: Make incomplete data and every exceptional outcome visible and distinguishable.

**Independent Test**: Simulate missing/partial inputs, invalid geometry, timeout, cancellation, empty results and a rendering failure, then verify status and user-visible explanation.

### Tests for User Story 5

- [ ] T035 [P] [US5] Verify partial source, unavailable source, timeout, cancellation and thrown-provider outcomes in `tests/AskLucy.Application.Tests/Spatial/Overlay/OverlayAnalysisSpecialistTests.cs`. Partial, unavailable, cancellation and thrown-provider cases are covered; timeout remains untested.
- [x] T036 [P] [US5] Add malformed and unsupported result-event feedback tests in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.test.ts` and `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.test.ts`
- [ ] T037 [P] [US5] Add visible 2D/3D render-failure feedback tests in the spatial overlay viewer component tests under `src/AskLucy.Web/ClientApp/src/features/viewer/components/`. 2D map load failure is covered; 3D failure remains unverified.

### Implementation for User Story 5

- [x] T038 Preserve per-source completeness, excluded/invalid counts, warnings, and operation failures through result normalization in `src/AskLucy.Application/Spatial/SpatialResultNormalizer.cs`
- [x] T039 Ensure overlay and malformed result events reach the existing visible chat error/retry path in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts` and `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts`
- [x] T040 Ensure asynchronous renderer failures show accessible status feedback instead of console-only errors in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayersOverlay.tsx`

**Checkpoint**: Every overlay ends in a visible result, clarification, cancellation or failure, with incomplete results labeled partial.

## Phase 8: Polish and Cross-Cutting Concerns

**Purpose**: Complete documented validation and verify the entire feature contract.

- [x] T041 [P] Update the spatial architecture overview to describe the additive bundle and normalized result contract in `docs/ARCHITECTURE.md`
- [ ] T042 Run all scenarios in `specs/097-overlay-analysis/quickstart.md`; focused local scenarios passed, while live browser/API-key, SQL Server, population-source, CRS-mismatch, and rendered-3D scenarios remain unavailable.
- [ ] T043 Run every backend and frontend check defined by `.github/workflows/ci.yml`; focused backend/frontend checks, backend release build, scoped format verification, frontend lint, and production build passed. Full repository test suites remain unverified.

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1) precedes foundational contract changes.
- Foundational contracts and validation (Phase 2) block every user story.
- US1 establishes candidate selection and eligibility. US2 builds on the same typed input/result contracts and provider.
- US3 depends on eligible candidates from US1; it does not change geometric eligibility.
- US4 depends on normalized result metadata from US1–US3 but can build its rendering against fixtures in parallel once Phase 2 completes.
- US5 spans provider, normalization, chat and renderer failures; it follows the relevant execution and viewer paths.
- Polish follows the desired stories.

### User Story Dependencies

- US1: After Phase 2; MVP for supported attribute, area and distance criteria.
- US2: After Phase 2; requires US1 provider/input contracts, but can be implemented as an independent operation set.
- US3: After US1 eligibility semantics.
- US4: After Phase 2 result/layer metadata; independent fixture-driven UI work can start after foundation.
- US5: After the execution and rendering paths it verifies exist.

### Parallel Opportunities

- T002 and T003 are independent contract files; T005 and T008 are separate tests once contracts are agreed.
- US1 specialist tests, provider tests, and capability parser tests touch separate files and can be written concurrently.
- US2 geometry engine tests and application validation tests are independent.
- US4 map renderer, legend and attribute-panel tasks touch separate components and tests.
- US5 provider outcome tests and client stream/render feedback tests can proceed in parallel after their contracts settle.

## Implementation Strategy

1. Complete the shared typed bundle, result, validation and client contract foundation.
2. Deliver US1 as the MVP: one bounded candidate-selection request with all hard criteria and complete coverage reporting.
3. Add the four operation semantics (US2), then transparent scoring (US3).
4. Complete result inspection and independent layers (US4) and explicit partial/failure behavior (US5).
5. Run the quickstart and CI checks after the cross-layer contract is integrated.

## Notes

- Task IDs are sequential; `[P]` indicates separate-file work that can proceed concurrently without an incomplete dependency.
- All story tasks have exact target files. Where an existing 3D test suite or architecture document path must be located at implementation time, preserve the specified existing integration and add the test/document beside its current owner rather than creating a duplicate subsystem.
