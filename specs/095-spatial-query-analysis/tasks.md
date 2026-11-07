# Tasks: Spatial Query Analysis

**Input**: Design documents from `specs/095-spatial-query-analysis/`

**Prerequisites**: [plan.md](plan.md) · [spec.md](spec.md) · [research.md](research.md) · [data-model.md](data-model.md) · [contracts/](contracts/) · [quickstart.md](quickstart.md)

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no shared dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)
- Exact file paths are included in every task description

## Path Conventions

- Backend Application: `src/AskLucy.Application/Spatial/`
- Backend Infrastructure: `src/AskLucy.Infrastructure/Spatial/Query/`
- Frontend: `src/AskLucy.Web/ClientApp/src/`
- Tests (Application): `src/AskLucy.Application.Tests/Spatial/Query/`
- Tests (Frontend): `src/AskLucy.Web/ClientApp/src/` (Vitest)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Register the new operation kind and extend the existing bundle models. No user story
work can begin until this phase is complete.

- [x] T001 Add `SpatialQueryParameters` record to `src/AskLucy.Application/Spatial/SpatialRequestBundle.cs` (fields: `DatasetId`, `AttributePredicates`, `AttributeCombinator`, `SpatialPredicates`, `SpatialCombinator`)
- [x] T002 Add `spatial.query` entry to `SpatialOperationKinds.Supported` in `src/AskLucy.Application/Spatial/SpatialRequestBundle.cs` with description, `WhenToUse`, and `ArgumentHint`
- [x] T003 Add `FeatureQuery` value to `SpatialLayerKind` enum in `src/AskLucy.Application/Spatial/SpatialAnalysisResult.cs`
- [x] T004 Create directory `src/AskLucy.Application/Spatial/Query/` and directory `src/AskLucy.Infrastructure/Spatial/Query/`

**Checkpoint**: Bundle model extended, new kind declared — specialist registration can now proceed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core Application domain types and abstractions that every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T005 [P] Create `AttributeField.cs` record in `src/AskLucy.Application/Spatial/Query/AttributeField.cs` (fields: `Name`, `DisplayName`, `FieldType` enum `Numeric|String|Year|Date`, `Unit`, `ExampleValue`)
- [x] T006 [P] Create `AttributePredicate.cs` record in `src/AskLucy.Application/Spatial/Query/AttributePredicate.cs` (fields: `Field`, `Operator` enum, `Value`, `ValueTo`, `Unit`; operator enum: `Gt Gte Lt Lte Eq NotEq Between Contains StartsWith EndsWith InYearRange`)
- [x] T007 [P] Create `SpatialPredicate.cs` record in `src/AskLucy.Application/Spatial/Query/SpatialPredicate.cs` (fields: `Relationship` enum `Within|Outside|Intersecting|Containing|Near`, `ReferenceKind` enum, `ReferenceName`, `ReferenceLayerId`, `ReferenceGeometry`, `DistanceMeters`)
- [x] T008 [P] Create `PredicateGroup.cs` generic record in `src/AskLucy.Application/Spatial/Query/PredicateGroup.cs` (fields: `Predicates IReadOnlyList<T>`, `Combinator` enum `And|Or`)
- [x] T009 [P] Create `DatasetEntry.cs` record in `src/AskLucy.Application/Spatial/Query/DatasetEntry.cs` (fields: `Id`, `DisplayName`, `AttributeFields`, `SupportedRelationships`, `ProviderId`)
- [x] T010 [P] Create `QueryResultFeature.cs` and `FeatureGeometry.cs` records in `src/AskLucy.Application/Spatial/Query/QueryResultFeature.cs` (fields per data-model.md)
- [x] T011 [P] Create `QueryResultBatch.cs` record in `src/AskLucy.Application/Spatial/Query/QueryResultBatch.cs` (fields: `BatchIndex`, `Features`, `TotalCount`, `EstimatedTotal`, `IsFinal`)
- [x] T012 Create `SpatialQueryDefinition.cs` record in `src/AskLucy.Application/Spatial/Query/SpatialQueryDefinition.cs` (fields: `QueryId`, `Dataset`, `AttributeGroup`, `SpatialGroup`, `StudyArea`, `BatchSize`, `TimeoutAt`) — depends on T005–T011
- [x] T013 Create `IDatasetCatalog.cs` interface in `src/AskLucy.Application/Spatial/Query/IDatasetCatalog.cs` (methods: `TryGet(string datasetId, out DatasetEntry entry)`, `ListAvailable()`)
- [x] T014 Create `DatasetCatalog.cs` in-memory implementation in `src/AskLucy.Application/Spatial/Query/DatasetCatalog.cs` (singleton, dictionary-backed, pre-populated with four initial `DatasetEntry` records for `land-parcels`, `buildings`, `roads`, `development-areas`)
- [x] T015 Create `ISpatialQueryProvider.cs` interface in `src/AskLucy.Application/Spatial/Query/ISpatialQueryProvider.cs` (`SupportedDatasetIds`, `ExecuteAsync` returning `IAsyncEnumerable<QueryResultBatch>`) per `contracts/spatial-query-provider.md`
- [x] T016 Register `IDatasetCatalog` / `DatasetCatalog` and `ISpatialQueryProvider` scan in `src/AskLucy.Application/DependencyInjection.cs`

**Checkpoint**: Domain types and abstractions complete — user story phases can now begin.

---

## Phase 3: User Story 1 — Attribute Filter Query (Priority: P1) 🎯 MVP

**Goal**: Users can ask Lucy to filter geographic features by one or more attribute conditions and see results on the map as a named, interactive layer.

**Independent Test**: Submit an attribute query with known test data (`buildings`, `height > 30`); confirm only matching features are returned and rendered, selecting a feature shows its attributes. Corresponds to quickstart Scenario 1.

### Implementation for User Story 1

- [x] T017 [P] [US1] Create `OverpassSpatialQueryProvider.cs` stub in `src/AskLucy.Infrastructure/Spatial/Query/OverpassSpatialQueryProvider.cs` implementing `ISpatialQueryProvider` for `buildings` and `land-parcels` datasets — streams `QueryResultBatch` using existing `IPoiQueryProvider` plumbing; attribute filtering applied server-side
- [x] T018 [P] [US1] Create `StaticDatasetQueryProvider.cs` stub in `src/AskLucy.Infrastructure/Spatial/Query/StaticDatasetQueryProvider.cs` implementing `ISpatialQueryProvider` for `roads` and `development-areas` — returns empty batches until real data source is wired; `IsFinal = true` immediately
- [x] T019 [P] [US1] Create `SpatialQueryAuditLogger.cs` in `src/AskLucy.Application/Spatial/Query/SpatialQueryAuditLogger.cs` — writes one `SpatialQueryExecuted` structured log entry per execution via `Serilog.ILogger` (fields per data-model `SpatialQueryAuditEntry`); accepts `SpatialQueryDefinition` + outcome + duration + feature IDs
- [x] T020 [US1] Create `SpatialQuerySpecialist.cs` in `src/AskLucy.Application/Spatial/Specialists/SpatialQuerySpecialist.cs` implementing `ISpatialSpecialist` with `OperationKind = "spatial.query"` — constructor-injects `IDatasetCatalog`, `ISpatialQueryProvider[]`, `SpatialQueryAuditLogger`; `ExecuteAsync` performs: (1) validate parameters, (2) resolve dataset, (3) build `SpatialQueryDefinition`, (4) pick provider, (5) iterate `IAsyncEnumerable` with 30-second `CancellationToken` deadline, (6) accumulate features into `SpatialLayerDraft` checkpoints, (7) write audit log, (8) return `SpatialSpecialistOutcome` — depends on T012–T019
- [x] T021 [US1] Register `SpatialQuerySpecialist` in `src/AskLucy.Application/DependencyInjection.cs` as `ISpatialSpecialist` alongside `PoiDiscoverySpecialist` and `ProximityAnalysisSpecialist`
- [x] T022 [US1] Add `FluentValidation` rules for `spatial.query` operation parameters in `src/AskLucy.Application/Spatial/SpatialBundleValidator.cs` — validate: `datasetId` resolves, at least one predicate group present, attribute field names exist on dataset, attribute operators valid for field type
- [x] T023 [US1] Extend `SpatialAnalysisResult.cs` / `SpatialResultNormalizer.cs` in `src/AskLucy.Application/Spatial/` to map `feature-query` layer kind through normalization — produce `SpatialLayerDraft` with `Kind = FeatureQuery`, `features[]`, `batchIndex`, `totalCount`, `isFinal`
- [x] T024 [US1] Add `'feature-query'` to `SpatialLayerKind` union and `validKinds` array in `src/AskLucy.Web/ClientApp/src/store/spatialLayersStore.ts`; add `attributes?: FeatureAttributes` to `SpatialMarkerPoint`; add `batchIndex`, `totalCount`, `estimatedTotal`, `isFinal` optional fields to `SpatialLayerPayload`; add `applyBatch()` action to store
- [x] T025 [US1] Extend `SpatialLayersOverlay.tsx` in `src/AskLucy.Web/ClientApp/src/features/siteAnalysis/SpatialLayersOverlay.tsx` to render `feature-query` layers — polygons from `features[].geometry.kind = "polygon"` using `ring`-equivalent rendering; points from `features[].geometry.kind = "point"` using existing marker rendering
- [x] T026 [US1] Create `FeatureAttributePanel.tsx` in `src/AskLucy.Web/ClientApp/src/features/siteAnalysis/FeatureAttributePanel.tsx` — MUI Drawer/Panel component; displays `feature.name` as title and iterates `feature.attributes` key-value pairs using dataset `AttributeField.displayName` labels; closes on backdrop click without altering the layer
- [x] T027 [US1] Create `spatialQueryExtension.ts` in `src/AskLucy.Web/ClientApp/src/viewer/extensions/spatialQueryExtension.ts` — viewer map click handler that checks if clicked entity belongs to a `feature-query` layer, reads its `attributes` from the store, and opens `FeatureAttributePanel` — depends on T026
- [x] T028 [US1] Extend `SpatialLayerLegend.tsx` in `src/AskLucy.Web/ClientApp/src/features/siteAnalysis/SpatialLayerLegend.tsx` to show running feature count badge and streaming progress indicator (spinner / "Loading…" label) while `isFinal = false`; replace with final count when `isFinal = true`

**Checkpoint**: US1 fully functional — attribute queries execute, results render on map, selecting a feature opens the attribute panel.

---

## Phase 4: User Story 2 — Spatial Relationship Query (Priority: P1)

**Goal**: Users can ask Lucy to find features based on a spatial relationship to another feature or area (within, outside, intersecting, containing, near).

**Independent Test**: With `roads` dataset and a known development zone boundary, submit an `intersecting` query; confirm only roads whose geometry crosses the zone are returned; `outside` query returns no overlapping roads. Corresponds to quickstart Scenario 2.

### Implementation for User Story 2

- [ ] T029 [US2] Add `SpatialPredicate` parsing to `SpatialQuerySpecialist.cs` in `src/AskLucy.Application/Spatial/Specialists/SpatialQuerySpecialist.cs` — resolve `referenceKind`: `namedFeature` via `ILocationResolutionService`, `activeLayer` from `SpatialExecutionContext.ActiveLayers`, `explicitGeometry` from bundle, `studyArea` from resolved context; populate `SpatialQueryDefinition.SpatialGroup`
- [ ] T030 [US2] Add spatial relationship validation to `SpatialBundleValidator.cs` in `src/AskLucy.Application/Spatial/SpatialBundleValidator.cs` — validate: relationship operator in target dataset's `SupportedRelationships`; `distanceMeters` present and positive when `relationship = Near`; reference geometry/name/layerId present for the stated `referenceKind`
- [ ] T031 [US2] Implement spatial predicate execution in `OverpassSpatialQueryProvider.cs` in `src/AskLucy.Infrastructure/Spatial/Query/OverpassSpatialQueryProvider.cs` — translate `SpatialPredicate` into spatial filter against retrieved geometries using existing `GeometryMath` helpers for `Within`, `Outside`, `Intersecting`, `Containing`; `Near` as distance ≤ `distanceMeters`
- [ ] T032 [US2] Add `distanceMeters` unit conversion helper in `src/AskLucy.Application/Spatial/Query/SpatialPredicate.cs` — convert user-supplied distance + unit to metres before the predicate is set on the definition (supports `m`, `km`, `ft`, `mi`)
- [ ] T033 [US2] Handle unresolvable reference feature in `SpatialQuerySpecialist.cs` — when `referenceKind = namedFeature` and `ILocationResolutionService` returns no result, return `SpatialSpecialistOutcome.Failed` with a focused clarification prompt (FR-005 / User Story 2 AS-3)

**Checkpoint**: US2 fully functional — spatial relationship queries execute independently of US1 attribute filter logic.

---

## Phase 5: User Story 3 — Combined Attribute and Spatial Query (Priority: P1)

**Goal**: Users can combine attribute predicates and spatial predicates in a single query and receive only features satisfying both simultaneously.

**Independent Test**: Configure test data where subset A satisfies the attribute condition, subset B satisfies the spatial condition, only subset A∩B satisfies both; submit combined query and confirm only A∩B is returned. Corresponds to quickstart Scenario 3.

### Implementation for User Story 3

- [ ] T034 [US3] Implement `PredicateGroup<T>` AND/OR combinator application in `SpatialQuerySpecialist.cs` / `src/AskLucy.Application/Spatial/Specialists/SpatialQuerySpecialist.cs` — when both `AttributeGroup` and `SpatialGroup` are non-null, intersect their result sets (AND); within each group apply the group's combinator (`And` → all predicates must pass; `Or` → any predicate must pass)
- [ ] T035 [US3] Detect and reject mixed-type OR in `SpatialBundleValidator.cs` / `src/AskLucy.Application/Spatial/SpatialBundleValidator.cs` — when coordinator interpretation detects the user phrased a query that would require OR-ing an attribute predicate with a spatial predicate, produce a `Failed` outcome with explanation: "I can combine attribute and spatial conditions with AND, but not with OR across types. Would you like me to run two separate queries instead?"
- [ ] T036 [US3] Implement combined result layer naming in `SpatialQuerySpecialist.cs` — derive `SpatialLayerDraft.Name` from both predicate groups: e.g., "Parcels > 5,000 m² within Al Khuwair Zone"; name must be human-readable and not expose internal field identifiers
- [ ] T037 [US3] Extend `SpatialAnalysisCoordinator.cs` interpretation wording in `src/AskLucy.Application/Spatial/SpatialAnalysisCoordinator.cs` — update Tier-1 interpretation guidance for `spatial.query` kind to cover combined queries ("parcels larger than X that are within Y")

**Checkpoint**: US3 functional — combined queries return only the intersection; mixed-type OR is handled gracefully.

---

## Phase 6: User Story 4 — Clarification for Incomplete Queries (Priority: P2)

**Goal**: When a query is missing required information, Lucy asks exactly one focused question and waits for the response before executing.

**Independent Test**: Submit a query missing geographic scope; confirm Lucy asks for the area and no result layer appears; provide the area; confirm the query executes and results appear without further prompting. Corresponds to quickstart Scenario 4.

### Implementation for User Story 4

- [ ] T038 [US4] Implement unrecognised dataset clarification in `SpatialQuerySpecialist.cs` / `src/AskLucy.Application/Spatial/Specialists/SpatialQuerySpecialist.cs` — when `datasetId` does not resolve, return `Failed` outcome with message listing available datasets from `IDatasetCatalog.ListAvailable()`, prompting user to choose (FR-005 / US4 AS-2)
- [ ] T039 [US4] Implement missing geographic scope clarification in `SpatialAnalysisCoordinator.cs` / `src/AskLucy.Application/Spatial/SpatialAnalysisCoordinator.cs` — when `spatial.query` operation has no `studyArea` and map context cannot supply one, ask "Which area should I search in?" before building the bundle (US4 AS-1)
- [ ] T040 [US4] Implement unit inference and confirmation in `SpatialQuerySpecialist.cs` — when attribute predicate has no `unit` and the field's canonical unit is unambiguous (e.g., m² for `area`), apply it and include the unit in `resolvedSummary` on the result (US4 AS-4); when unit is genuinely ambiguous, ask the user (FR-005)
- [ ] T041 [US4] Implement ambiguous attribute field clarification in `SpatialQuerySpecialist.cs` — when `AttributePredicate.Field` does not match any `AttributeField.Name` on the target dataset, return `Failed` outcome listing available fields and asking the user to choose (US1 AS-4)

**Checkpoint**: US4 functional — no query ever executes with unresolvable parameters; every missing piece is surfaced as a focused single question.

---

## Phase 7: User Story 5 — Result Layer Management (Priority: P2)

**Goal**: Each query result occupies its own named, independently controllable map layer that persists across 2D/3D view switches; feature selection opens the attribute panel.

**Independent Test**: Run two successive queries, verify independent named layers; hide one, remove the other; switch 2D↔3D; confirm layers and visibility state are preserved; click a feature to open attribute panel. Corresponds to quickstart Scenarios 5 and 6.

### Implementation for User Story 5

- [ ] T042 [US5] Implement `applyBatch()` action fully in `src/AskLucy.Web/ClientApp/src/store/spatialLayersStore.ts` — on `batchIndex = 0`: create new layer entry (or replace same `layerId` from same analysis); on `batchIndex > 0`: append `features` to existing layer's list; update `totalCount`, `isFinal`; existing `toggleLayer`, `removeLayer`, `clearAnalysis`, `clearLayers` actions work unchanged
- [ ] T043 [US5] Wire `__SPATIAL_LAYERS__` event parser in the chat stream handler to call `applyBatch()` when `batchIndex` is present, or existing `applyLayers()` for non-batch events — locate the SSE event handler in `src/AskLucy.Web/ClientApp/src/` and extend it; backward-compatible (non-batch events unaffected)
- [ ] T044 [US5] Implement 2D/3D view persistence in `SpatialLayersOverlay.tsx` / `src/AskLucy.Web/ClientApp/src/features/siteAnalysis/SpatialLayersOverlay.tsx` — ensure `feature-query` layer geometries survive viewer mode switch by reading from the store (which is not view-scoped) rather than viewer-local state
- [ ] T045 [US5] Add "Clear all analysis layers" button to `SpatialLayerLegend.tsx` / `src/AskLucy.Web/ClientApp/src/features/siteAnalysis/SpatialLayerLegend.tsx` if not already present; confirm it calls `clearLayers()` and the legend becomes empty after confirmation

**Checkpoint**: US5 functional — layer lifecycle controls (hide/remove/clear) work independently per layer; 2D/3D switch preserves state; feature attribute panel opens on click.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Audit log, timeout handling, error surfaces, and validation completeness that affect all user stories.

- [ ] T046 [P] Implement 30-second `CancellationToken` deadline in `SpatialQuerySpecialist.cs` / `src/AskLucy.Application/Spatial/Specialists/SpatialQuerySpecialist.cs` — create `CancellationTokenSource` linked to the outer token with a 30-second absolute deadline; on `OperationCanceledException` after partial batches, emit final layer with `isFinal = true` and append a timeout warning to `SpatialSpecialistOutcome.Warnings`
- [ ] T047 [P] Implement loading indicator in `SpatialLayerLegend.tsx` / `src/AskLucy.Web/ClientApp/src/features/siteAnalysis/SpatialLayerLegend.tsx` — show spinner + "Loading…" + running count while `isFinal = false`; on timeout: show "Partial results — query timed out" notice; on completion: show final count without spinner
- [ ] T048 [P] Implement duplicate query detection in `SpatialAnalysisCoordinator.cs` / `src/AskLucy.Application/Spatial/SpatialAnalysisCoordinator.cs` — when an identical `spatial.query` bundle is submitted while an identically-named layer already exists, ask "This query already has results on the map. Would you like to re-run it or use the existing results?" (edge case — spec Edge Cases)
- [ ] T049 [P] Implement large-result-set warning in `SpatialQuerySpecialist.cs` — when `estimatedTotal` from the first batch exceeds the planning-defined warning threshold (default: 2,000 features), include a user-visible notice in `SpatialSpecialistOutcome.Warnings` before streaming continues
- [ ] T050 [P] Implement attribute field unavailability error in `SpatialQuerySpecialist.cs` — when the queried dataset has no features with the requested attribute field, return `Failed` with message "The [dataset] dataset doesn't have a [field] field. Available fields are: [list]" (edge case — spec Edge Cases)
- [ ] T051 Validate the full audit log pipeline end-to-end: run quickstart Scenario 8 and confirm `SpatialQueryExecuted` entries appear in the structured log with all required fields (FR-015 / SC-009)
- [ ] T052 [P] Write unit tests for `SpatialQuerySpecialist` in `src/AskLucy.Application.Tests/Spatial/Query/SpatialQuerySpecialistTests.cs` — cover: empty result, timeout cancellation, provider failure, audit log call, attribute-only query, spatial-only query, combined query, mixed-type OR rejection
- [ ] T053 [P] Write unit tests for `DatasetCatalog` in `src/AskLucy.Application.Tests/Spatial/Query/DatasetCatalogTests.cs` — cover: known dataset resolves, unknown dataset returns false, `ListAvailable` returns all four entries
- [ ] T054 [P] Write unit tests for `AttributePredicate` / `SpatialPredicate` parsing and validation in `src/AskLucy.Application.Tests/Spatial/Query/AttributePredicateTests.cs` and `SpatialPredicateTests.cs`
- [ ] T055 [P] Write Vitest tests for `spatialLayersStore.ts` batch accumulation in `src/AskLucy.Web/ClientApp/src/store/spatialLayersStore.batch.test.ts` — cover: first batch creates layer, subsequent batches append, `isFinal` dismisses progress indicator, `removeLayer` removes single layer, `clearLayers` removes all
- [ ] T056 [P] Write Vitest tests for `FeatureAttributePanel.tsx` in `src/AskLucy.Web/ClientApp/src/features/siteAnalysis/FeatureAttributePanel.test.tsx` — cover: renders feature name and all attributes, closing panel fires `onClose`, does not alter layer state
- [ ] T057 Run all quickstart.md validation scenarios (Scenarios 1–8) and confirm all pass

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — **BLOCKS all user story phases**
- **US1 (Phase 3)**: Depends on Phase 2 — no dependency on US2–US5
- **US2 (Phase 4)**: Depends on Phase 2 — no dependency on US1 (shares the specialist file; coordinate file edits)
- **US3 (Phase 5)**: Depends on US1 and US2 (combines both predicate types)
- **US4 (Phase 6)**: Depends on Phase 2 — coordinate with US1 and US2 (adds error branches to the specialist)
- **US5 (Phase 7)**: Depends on Phase 3 (US1) for the `applyBatch` action and `feature-query` layer kind
- **Polish (Phase 8)**: Depends on all user story phases complete

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 — independent MVP
- **US2 (P1)**: Can start after Phase 2 — shares `SpatialQuerySpecialist.cs`; coordinate file edits with US1 developer
- **US3 (P1)**: Requires US1 and US2 complete (uses both predicate group types)
- **US4 (P2)**: Can start after Phase 2; adds error/clarification branches; coordinate with US1–US2
- **US5 (P2)**: Requires US1 (Phase 3) for `applyBatch` and `feature-query` layer kind

### Within Each Phase

- `[P]`-marked tasks have no dependency on each other within the same phase and can run in parallel
- Non-`[P]` tasks depend on earlier tasks in the same phase as listed

---

## Parallel Opportunities

### Phase 2 (Foundational)

```
T005 AttributeField.cs     ─┐
T006 AttributePredicate.cs  │
T007 SpatialPredicate.cs    ├─ All parallel (independent files)
T008 PredicateGroup.cs      │
T009 DatasetEntry.cs        │
T010 QueryResultFeature.cs  │
T011 QueryResultBatch.cs   ─┘
         ↓ (all complete)
T012 SpatialQueryDefinition.cs   ─┐ sequential
T013 IDatasetCatalog.cs          ─┘
T014 DatasetCatalog.cs
T015 ISpatialQueryProvider.cs
T016 DI registration
```

### Phase 3 (US1)

```
T017 OverpassSpatialQueryProvider.cs  ─┐
T018 StaticDatasetQueryProvider.cs     │ Parallel (different files)
T019 SpatialQueryAuditLogger.cs       ─┘
         ↓
T020 SpatialQuerySpecialist.cs  (depends on T017–T019)
T021 DI registration
T022 BundleValidator rules
T023 ResultNormalizer extension
         ↓
T024 spatialLayersStore.ts extension   ─┐
T025 SpatialLayersOverlay.tsx extension │ Parallel (different files)
T026 FeatureAttributePanel.tsx (new)   ─┘
         ↓
T027 spatialQueryExtension.ts (depends on T026)
T028 SpatialLayerLegend.tsx extension
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T004)
2. Complete Phase 2: Foundational (T005–T016) — **CRITICAL, blocks all stories**
3. Complete Phase 3: US1 — Attribute Filter Query (T017–T028)
4. **STOP and VALIDATE**: Run quickstart Scenario 1 end-to-end
5. **Demo**: Attribute filter queries working with map visualization and attribute panel

### Incremental Delivery

1. Setup + Foundational → framework ready
2. Add US1 (Attribute Filter) → test → **MVP demo** ✅
3. Add US2 (Spatial Relationship) → test → demo combined specialist
4. Add US3 (Combined Query) → test → full query capability
5. Add US4 (Clarification) → test → production-safe error handling
6. Add US5 (Layer Management) → test → full layer lifecycle
7. Polish (Phase 8) → audit log verified, timeouts tested, edge cases covered

### Parallel Team Strategy

With two developers after Phase 2:

- **Developer A**: US1 (T017–T028) — full attribute query stack
- **Developer B**: US4 (T038–T041) — clarification flows (uses Phase 2 types; `SpatialQuerySpecialist.cs` coordinate with Dev A)

After US1 + US2 complete:

- **Developer A**: US3 (T034–T037) — combined predicates
- **Developer B**: US5 (T042–T045) — layer management and streaming

---

## Notes

- `[P]` tasks operate on different files and have no shared write dependency within their phase
- `[Story]` label maps each task to a specific user story for traceability and independent delivery
- `SpatialQuerySpecialist.cs` is touched by US1, US2, US3, and US4 — coordinate concurrent edits; consider feature-branch merges per story
- The `OverpassSpatialQueryProvider` for spatial predicates (T031) requires geometry intersection logic from existing `GeometryMath` helpers — verify availability before starting US2
- Commit after each phase checkpoint and validate independently before moving to the next phase
- Avoid modifying `SpatialAnalysisCoordinator.cs` core dispatch logic — all integration is via specialist registration only (spec 094 FR-028 guarantee)
