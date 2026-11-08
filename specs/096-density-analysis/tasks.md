# Tasks: Density Analysis

**Input**: Design documents from `/specs/096-density-analysis/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required by the feature specification and project constitution. Write tests before the associated implementation and confirm they fail for the missing behavior.

**Organization**: User-story phases are independently testable after the shared foundation is complete.

## Phase 1: Setup

**Purpose**: Establish the density feature's source/test layout and contract fixtures.

- [ ] T001 Create the Application density feature folder and test folders at `src/AskLucy.Application/Spatial/Density/`, `tests/AskLucy.Application.Tests/Spatial/Density/`, and `tests/AskLucy.Infrastructure.Tests/Spatial/Density/`
- [ ] T002 [P] Create deterministic point-cluster, sparse-grid, duplicate, boundary, and population-area fixtures in `tests/AskLucy.Application.Tests/Spatial/Density/DensityTestData.cs`
- [ ] T003 [P] Create frontend density surface payload fixtures in `src/AskLucy.Web/ClientApp/src/features/viewer/components/densitySurfaceFixtures.ts`
- [ ] T004 Document the supported density test datasets and expected units in `specs/096-density-analysis/quickstart.md`

---

## Phase 2: Foundational Contracts and Integration

**Purpose**: Add the validated contract and transport foundation that blocks all density stories.

**⚠️ CRITICAL**: Complete this phase before user-story work.

- [ ] T005 [P] Add `density.analysis`, `DensityAnalysisParameters`, density method/intent/classification enums, and backward-compatible operation serialization in `src/AskLucy.Application/Spatial/SpatialRequestBundle.cs`
- [ ] T006 [P] Define `DensityDatasetEntry`, `IDensityDatasetCatalog`, `DensityAnalysisDefinition`, `DensityCell`, `DensityClassificationResult`, `DensityAnalysisResult`, `DensityVisualizationPreferences`, and `IDensityAnalysisProvider` in `src/AskLucy.Application/Spatial/Density/`
- [ ] T007 [P] Add validator tests for missing study area, unknown dataset, incompatible method, invalid/finite radius and cell size, and invalid visualization range in `tests/AskLucy.Application.Tests/Spatial/Density/DensityDefinitionValidatorTests.cs`
- [ ] T008 Extend density validation and contextual user-visible failure mapping in `src/AskLucy.Application/Spatial/SpatialBundleValidator.cs`
- [ ] T009 [P] Add a typed `DensitySurface` layer kind and density payload fields, validating cells, values, classifications, and metadata in `src/AskLucy.Application/Spatial/SpatialAnalysisResult.cs` and `src/AskLucy.Application/Spatial/SpatialResultNormalizer.cs`
- [ ] T010 [P] Add normalization contract tests for valid density surfaces and rejected invalid geometry/value/classification payloads in `tests/AskLucy.Application.Tests/Spatial/SpatialResultNormalizerTests.cs`
- [ ] T011 Extend density operation input schema, parsing, clarification prompts, and layer-command mapping through the sole conversational entry in `src/AskLucy.Application/Conversations/Capabilities/SpatialAnalysisCapability.cs`
- [ ] T012 Add density operation parsing and clarification tests in `tests/AskLucy.Application.Tests/Conversations/Capabilities/SpatialAnalysisCapabilityTests.cs`
- [ ] T013 Register the density catalog, audit logger, and `DensityAnalysisSpecialist` in `src/AskLucy.Application/DependencyInjection.cs`
- [ ] T014 Add `density-surface` TypeScript contracts, strict payload validation, and a per-layer display-preferences update action in `src/AskLucy.Web/ClientApp/src/store/spatialLayersStore.ts`
- [ ] T015 Add store tests for density payload rejection, immutable calculated values, valid display updates, and independent layer lifecycle in `src/AskLucy.Web/ClientApp/src/store/spatialLayersStore.test.ts`
- [ ] T016 Propagate malformed `__SPATIAL_LAYERS__` events as a user-visible retryable chat error rather than console-only warning in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts` and `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts`
- [ ] T017 Add malformed density-layer stream handling tests in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.test.ts` and `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.test.ts`

**Checkpoint**: Density bundles, validated layer payloads, conversation routing, and client state are ready. All stories can start.

---

## Phase 3: User Story 1 — Identify Concentration Hotspots (Priority: P1) 🎯 MVP

**Goal**: Users can request point-count or point-kernel density, receive correct hotspots, and view an interactive density surface.

**Independent Test**: Submit a hotspot question against a known clustered point fixture; verify the correct high-value cells/classification, disclosed effective values, accessible legend, and named surface layer.

### Tests for User Story 1

- [ ] T018 [P] [US1] Add point-count and point-kernel expected-value, duplicate, and boundary tests in `tests/AskLucy.Application.Tests/Spatial/Density/DensityCalculationTests.cs`
- [ ] T019 [P] [US1] Add specialist tests for successful hotspot result, defaults disclosure, unavailable source, empty result, and timeout in `tests/AskLucy.Application.Tests/Spatial/Density/DensityAnalysisSpecialistTests.cs`
- [ ] T020 [P] [US1] Add Infrastructure provider fixture tests for bounded point retrieval, cancellation, and source coverage in `tests/AskLucy.Infrastructure.Tests/Spatial/Density/OverpassDensityAnalysisProviderTests.cs`
- [ ] T021 [P] [US1] Add density surface map-render and selection tests in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayersOverlay.test.tsx`
- [ ] T022 [P] [US1] Add accessible legend metadata and hotspot surface assertions in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayerLegend.test.tsx`

### Implementation for User Story 1

- [ ] T023 [US1] Implement catalog lookup, method compatibility, effective defaulting, deduplication, unit assignment, and density-classification policy in `src/AskLucy.Application/Spatial/Density/DensityDatasetCatalog.cs`, `src/AskLucy.Application/Spatial/Density/DensityCalculationPolicy.cs`, and `src/AskLucy.Application/Spatial/Density/DensityClassifier.cs`
- [ ] T024 [US1] Implement `DensityAnalysisSpecialist` with resolved bounded scope, linked deadline/cancellation, complete/partial/empty/failed outcomes, and normalized density surface draft creation in `src/AskLucy.Application/Spatial/Specialists/DensityAnalysisSpecialist.cs`
- [ ] T025 [US1] Implement the point-source adapter behind `IDensityAnalysisProvider` in `src/AskLucy.Infrastructure/Spatial/Density/OverpassDensityAnalysisProvider.cs`
- [ ] T026 [US1] Register the Infrastructure density provider and configured catalog entries in `src/AskLucy.Infrastructure/DependencyInjection.cs`
- [ ] T027 [US1] Render `density-surface` cells as graduated polygons or supported heatmap output, with cleanup and cell inspection events, in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayersOverlay.tsx`
- [ ] T028 [US1] Add the density method/unit/effective-parameter/coverage legend and hotspot labels in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayerLegend.tsx`
- [ ] T029 [US1] Wire density cell inspection into the existing feature inspection pattern in `src/AskLucy.Web/ClientApp/src/features/viewer/components/FeatureAttributePanel.tsx` and `src/AskLucy.Web/ClientApp/src/viewer/extensions/spatialQueryExtension.ts`

**Checkpoint**: A point-density hotspot request is conversationally explained, accurately calculated, displayed, inspectable, and independently manageable.

---

## Phase 4: User Story 2 — Find Low-Coverage Areas (Priority: P1)

**Goal**: Users can find low-density service areas and calculate population density only from compatible population-area datasets.

**Independent Test**: Run low-coverage and population-density requests over fixed sparse/dense and population-area fixtures, confirming low classes, units, caveats, and incompatible-source failure.

### Tests for User Story 2

- [ ] T030 [P] [US2] Add low-density classification, flat distribution, and concentration-caveat tests in `tests/AskLucy.Application.Tests/Spatial/Density/DensityClassificationTests.cs`
- [ ] T031 [P] [US2] Add population-density tests for population/area calculation, zero area, missing value, and point-only incompatibility in `tests/AskLucy.Application.Tests/Spatial/Density/PopulationDensityTests.cs`
- [ ] T032 [P] [US2] Add low-coverage and population layer/legend rendering tests in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayerLegend.test.tsx`

### Implementation for User Story 2

- [ ] T033 [US2] Extend `DensityCalculationPolicy` and `DensityClassifier` to derive low-density summaries, flat-distribution warnings, and non-causal conversational caveats in `src/AskLucy.Application/Spatial/Density/DensityCalculationPolicy.cs` and `src/AskLucy.Application/Spatial/Density/DensityClassifier.cs`
- [ ] T034 [US2] Implement population dataset metadata validation and the explicit population provider/unavailable outcome seam in `src/AskLucy.Application/Spatial/Density/DensityDatasetCatalog.cs` and `src/AskLucy.Infrastructure/Spatial/Density/StaticPopulationDensityProvider.cs`
- [ ] T035 [US2] Extend `DensityAnalysisSpecialist` to return population density and incompatible-source outcomes with coverage/exclusion details in `src/AskLucy.Application/Spatial/Specialists/DensityAnalysisSpecialist.cs`
- [ ] T036 [US2] Display low-density and population-specific legend copy, units, warnings, and inspection fields in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayerLegend.tsx` and `src/AskLucy.Web/ClientApp/src/features/viewer/components/FeatureAttributePanel.tsx`

**Checkpoint**: Low-coverage and population requests are correct, explicitly caveated, and independently testable without a hotspot request.

---

## Phase 5: User Story 3 — Adjust a Density Layer (Priority: P2)

**Goal**: Users can safely restyle a density result or launch a clearly labelled recalculation while preserving independent layer controls.

**Independent Test**: Create two surfaces, alter display properties on one, then alter its calculation parameters; verify values stay stable for display-only edits, recalculation creates a distinct labelled result, and other layer state is unaffected.

### Tests for User Story 3

- [ ] T037 [P] [US3] Add display-setting validation and no-source-value-mutation store tests in `src/AskLucy.Web/ClientApp/src/store/spatialLayersStore.test.ts`
- [ ] T038 [P] [US3] Add accessible palette/range/classification control and independent hide/remove tests in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayerLegend.test.tsx`
- [ ] T039 [P] [US3] Add recalculation bundle/default/effective-parameter audit tests in `tests/AskLucy.Application.Tests/Spatial/Density/DensityAnalysisSpecialistTests.cs`

### Implementation for User Story 3

- [ ] T040 [US3] Add density visualization controls with accessibility labels, validation feedback, and per-layer update dispatch in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayerLegend.tsx`
- [ ] T041 [US3] Apply palette, opacity, visible-range, and classification-display state during density rendering without mutating cells in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayersOverlay.tsx`
- [ ] T042 [US3] Route analysis-parameter changes through a new validated density operation, producing a new named layer and audit event in `src/AskLucy.Application/Conversations/Capabilities/SpatialAnalysisCapability.cs` and `src/AskLucy.Application/Spatial/Specialists/DensityAnalysisSpecialist.cs`

**Checkpoint**: Per-layer visual changes are reversible and isolated; calculation changes are reproducible analyses rather than hidden mutations.

---

## Phase 6: User Story 4 — Receive Trustworthy Results and Failures (Priority: P2)

**Goal**: All density data limitations and technical failures end with a visible, honest outcome.

**Independent Test**: Force invalid inputs, no data, partial coverage, cancellation, timeout, malformed payload, and renderer exception; verify each is surfaced with no stale or misleading density layer.

### Tests for User Story 4

- [ ] T043 [P] [US4] Add audit-entry completeness tests for success, empty, partial, failed, and cancelled density operations in `tests/AskLucy.Application.Tests/Spatial/Density/DensityAnalysisAuditLoggerTests.cs`
- [ ] T044 [P] [US4] Add coordinator integration tests for density partial/failure outcome and no-layer behavior in `tests/AskLucy.Application.Tests/Spatial/SpatialAnalysisCoordinatorTests.cs`
- [ ] T045 [P] [US4] Add viewer render-error visible-feedback and retry tests in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayersOverlay.test.tsx`

### Implementation for User Story 4

- [ ] T046 [US4] Implement immutable structured density audit logging with resolved/effective input, coverage, result summary, source provenance, duration, and terminal status in `src/AskLucy.Application/Spatial/Density/DensityAnalysisAuditLogger.cs`
- [ ] T047 [US4] Add explicit data-quality, empty, partial, cancellation, timeout, and provider/calculation failure translation in `src/AskLucy.Application/Spatial/Specialists/DensityAnalysisSpecialist.cs`
- [ ] T048 [US4] Replace map renderer console-only error handling with accessible visible feedback and a retry/re-run action in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayersOverlay.tsx`
- [ ] T049 [US4] Ensure density stream/payload failures reach the active chat UI as visible retryable errors in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts` and `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts`

**Checkpoint**: Every tested failure has a user-visible outcome, honest source-coverage status, and operator-verifiable audit trail.

---

## Phase 7: Polish and Cross-Cutting Validation

**Purpose**: Verify performance, accessibility, contracts, and documentation across all stories.

- [ ] T050 [P] Add performance-budget tests for normal-area calculation and 10-second visible-result target in `tests/AskLucy.Application.Tests/Spatial/Density/DensityPerformanceTests.cs`
- [ ] T051 [P] Add 2D/3D continuity and layer-clear regression coverage in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayersOverlay.test.tsx` and `src/AskLucy.Web/ClientApp/src/store/spatialLayersStore.test.ts`
- [ ] T052 [P] Run accessibility assertions for legends, controls, values, warnings, and retry feedback in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayerLegend.test.tsx` and `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayersOverlay.test.tsx`
- [ ] T053 Validate all quickstart scenarios and record any configuration prerequisites in `specs/096-density-analysis/quickstart.md`
- [ ] T054 Verify contract-to-code conformance against `specs/096-density-analysis/contracts/density-operation-parameters.md`, `specs/096-density-analysis/contracts/density-analysis-provider.md`, and `specs/096-density-analysis/contracts/density-surface-layer-event.md`
- [ ] T055 Run affected backend and ClientApp test suites, formatting/lint checks, and architecture checks from `tests/AskLucy.Application.Tests/AskLucy.Application.Tests.csproj`, `tests/AskLucy.Infrastructure.Tests/AskLucy.Infrastructure.Tests.csproj`, and `src/AskLucy.Web/ClientApp/package.json`; resolve all failures before review

---

## Dependencies and Execution Order

1. Phase 1 → Phase 2 is mandatory.
2. US1 and US2 require Phase 2. US2's low-coverage point path can proceed independently; its population path reuses the shared density types.
3. US3 requires the surface/store from US1 and calculation definition from Phase 2.
4. US4 requires Phase 2 and can run in parallel with US1–US3 once common outcome types exist.
5. Phase 7 follows all intended user-story work.

### Parallel Opportunities

- T002–T004, T005–T007, T009–T010, and T014–T015 can proceed in parallel when their target files differ.
- US1 tests T018–T022 are parallel; US2 tests T030–T032, US3 tests T037–T039, and US4 tests T043–T045 are parallel.
- After Phase 2, backend policy/provider work and client renderer/legend work can proceed concurrently, coordinating only on the density-surface contract.

## Implementation Strategy

### MVP

Complete Phases 1–3: validated point-count/kernel hotspot analysis, a fixture-backed provider, `density-surface` transport, visible map layer, accessible legend, and no-silent-failure behavior. Validate US1 before moving on.

### Incremental Delivery

1. Foundation → contract-safe density transport.
2. US1 → hotspots and first useful density map.
3. US2 → low coverage and compatible population density.
4. US3 → parameter/display control and reproducible recalculation.
5. US4 → exhaustive failure/audit assurance.
6. Polish → performance, accessibility, contract, and quickstart verification.
