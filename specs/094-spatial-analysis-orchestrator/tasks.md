# Tasks: Spatial Request Bundle & Analysis Orchestrator

**Input**: Design documents from `/specs/094-spatial-analysis-orchestrator/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included because constitution §10 requires unit/integration tests and plan.md specifies the coverage.
Write story tests first and confirm they fail before implementation.

**Organization**: Tasks are grouped by user story (US1–US6) so each is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US6, matching `spec.md`
- Every task names an exact file path

## Path Conventions

- Backend: `src/AskLucy.Application/`, `src/AskLucy.Web/`, tests under `tests/`
- Frontend: `src/AskLucy.Web/ClientApp/src/`
- Feature docs: `specs/094-spatial-analysis-orchestrator/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the feature surface and record the discovery baseline.

- [X] T001 Confirm no existing `SpatialRequestBundle`/coordinator type (search `src/` for "SpatialRequestBundle", "SpatialAnalysisCoordinator", "ISpatialSpecialist") and create `src/AskLucy.Application/Spatial/` + `src/AskLucy.Application/Spatial/Specialists/`; record the search result in `specs/094-spatial-analysis-orchestrator/research.md`.
- [X] T002 [P] Add `Spatial` to the `SubAgentArea` enum in `src/AskLucy.Application/Conversations/Capabilities/IConversationCapability.cs`.
- [X] T003 [P] Create test folders `tests/AskLucy.Application.Tests/Spatial/` and `tests/AskLucy.Web.Tests/Spatial/`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Contracts, models, registry, state, and the delivery channel every story needs.

**⚠️ CRITICAL**: Complete this phase before starting any user story.

- [X] T004 [P] Create bundle models in `src/AskLucy.Application/Spatial/SpatialRequestBundle.cs`: `SpatialRequestBundle`, `SpatialOperation`, `FeatureSelector`, `SpatialPreferences`, `MapViewportContext`, `StudyAreaReference`, `ResolvedStudyArea`, `SpatialBundleVersion`, and `SpatialOperationKinds` constants — exactly the fields and rules in `data-model.md`.
- [X] T005 [P] Create result/layer models in `src/AskLucy.Application/Spatial/SpatialAnalysisResult.cs` and `src/AskLucy.Application/Spatial/SpatialLayer.cs`: `SpatialAnalysisResult`, `SpatialAnalysisStatus`, `SpatialOperationOutcome`, `SpatialOperationStatus`, `DataSourceCoverage`, `SpatialLayer`, `SpatialLayerKind`, `SpatialLayerStyle`, `SpatialLayerDraft`.
- [X] T006 [P] Create the specialist contract in `src/AskLucy.Application/Spatial/ISpatialSpecialist.cs`: `ISpatialSpecialist`, `SpatialExecutionContext`, `SpatialSpecialistOutcome`, `ActiveSpatialLayer`, per `contracts/spatial-specialist.md`.
- [X] T007 Create `src/AskLucy.Application/Spatial/SpatialSpecialistRegistry.cs` built from `IEnumerable<ISpatialSpecialist>`, keyed case-insensitively by `OperationKind`, throwing a clear startup error on duplicate kinds.
- [X] T008 [P] Implement `src/AskLucy.Application/Spatial/SpatialBundleValidator.cs` (FluentValidation): supported version, ≥1 operation, unique ids, acyclic `dependsOn`, known kinds, study-area presence, finite/in-range map context, positive thresholds — the validation order in `contracts/spatial-request-bundle.md`.
- [X] T009 [P] Implement `src/AskLucy.Application/Spatial/SpatialResultNormalizer.cs`: drafts → validated `SpatialLayer`s, geometry/coordinate/attribute allowlist checks, de-duplication by stable feature id, status/coverage/warning assembly.
- [X] T010 [P] Implement `src/AskLucy.Application/Spatial/SpatialLayerStateStore.cs` by generalizing `src/AskLucy.Application/PointsOfInterest/PoiLayerStateStore.cs`: layers keyed by `analysisId` + `layerId`, pending keep/replace and which-area questions, 10-minute expiry, `Sync` from client-reported active layers.
- [X] T011 Implement `src/AskLucy.Application/Spatial/StudyAreaResolver.cs`: resolve in order explicit location → active boundary → map viewport → ask; return a `ResolvedStudyArea` or a clarification outcome; enforce bounded areas.
- [X] T012 [P] Add `MapViewportContext? MapContext` to `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommand.cs` and to the chat request DTO used by `src/AskLucy.Web/Controllers/v1/AiController.cs`.
- [X] T013 [P] Add `MapViewportContext? MapContext` to `src/AskLucy.Application/Conversations/Capabilities/TurnContext.cs` and populate it in `src/AskLucy.Application/Conversations/Capabilities/TurnContextFactory.cs`.
- [X] T014 [P] Create `src/AskLucy.Application/Viewer/SpatialLayerCommand.cs`; add `SpatialLayers` to `src/AskLucy.Application/Ai/Commands/SendChatMessage/ChatStreamChunk.cs`; map capability output to it in `src/AskLucy.Application/Conversations/Runtime/StructuredPayloadExtractor.cs`.
- [X] T015 [P] Add `WriteSpatialLayersEventAsync` to `src/AskLucy.Web/Controllers/v1/AiController.cs` emitting `data: __SPATIAL_LAYERS__{json}\n\n` and flushing mid-stream, following `WritePoiLayerEventAsync`; call it from the chunk loop.
- [X] T016 Register `SpatialSpecialistRegistry`, `SpatialBundleValidator`, `SpatialResultNormalizer`, `SpatialLayerStateStore`, `StudyAreaResolver`, and every `ISpatialSpecialist` in `src/AskLucy.Application/DependencyInjection.cs` (register specialists directly against the interface, never a self-referential `GetRequiredService<T>` factory).

**Checkpoint**: Models, registry, state and delivery channel are ready. User-story work can begin.

---

## Phase 3: User Story 1 — One coordinated answer (Priority: P1) 🎯 MVP

**Goal**: Every POI/spatial question goes through one coordinator and renders through one event.

**Independent Test**: Ask "Find hospitals in Muscat"; exactly one `__SPATIAL_LAYERS__` event with a `markers` layer appears, and no `__POI_LAYERS__` event exists.

### Tests for User Story 1

- [X] T017 [P] [US1] Unit tests for bundle validation and registry lookup in `tests/AskLucy.Application.Tests/Spatial/SpatialBundleValidatorTests.cs`.
- [X] T018 [P] [US1] Unit tests for the POI specialist (category resolution, provider failure ≠ empty) in `tests/AskLucy.Application.Tests/Spatial/PoiDiscoverySpecialistTests.cs`.
- [X] T019 [P] [US1] Contract test from capability output to `ChatStreamChunk.SpatialLayers` and the `__SPATIAL_LAYERS__` payload in `tests/AskLucy.Web.Tests/Spatial/SpatialLayersEventTests.cs`.
- [X] T020 [P] [US1] Frontend tests for the new store and SSE parsing in `src/AskLucy.Web/ClientApp/src/store/spatialLayersStore.test.ts` and `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.test.ts`.

### Implementation for User Story 1

- [X] T021 [US1] Implement `src/AskLucy.Application/Spatial/SpatialAnalysisCoordinator.cs`: interpret a single-operation bundle, validate, resolve the specialist, dispatch, normalize, and return a `SpatialAnalysisResult`.
- [X] T022 [US1] Implement `src/AskLucy.Application/Spatial/Specialists/PoiDiscoverySpecialist.cs` by moving the retrieval part of `src/AskLucy.Application/Conversations/Capabilities/FindPointsOfInterestCapability.cs` (category resolution, `IPoiQueryProvider`, area radius/bounds) behind `ISpatialSpecialist`; it returns features and coverage, never a question or layer.
- [X] T023 [US1] Implement `src/AskLucy.Application/Conversations/Capabilities/SpatialAnalysisCapability.cs`: Tier-1 `Description`/`WhenToUse`/`ArgumentHint` for all supported spatial asks, Tier-2 guidance, input/output schema, `ExecuteAsync` → coordinator → `AgentToolResult` containing the normalized result.
- [X] T024 [US1] Remove `FindPointsOfInterestCapability` from `src/AskLucy.Application/DependencyInjection.cs` and delete the file after porting its tests to `SpatialAnalysisCapability`/`PoiDiscoverySpecialist`.
- [X] T025 [US1] Create `src/AskLucy.Web/ClientApp/src/store/spatialLayersStore.ts` (markers kind, `replace`/`keep`, cross-chat isolation) and `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayersOverlay.tsx`; contribute it from `src/AskLucy.Web/ClientApp/src/viewer/extensions/builtin/poiMarkerExtension.tsx`.
- [X] T026 [US1] Parse `__SPATIAL_LAYERS__` in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts` and apply it in both dispatch branches of `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts`; remove the `__POI_LAYERS__` parser.
- [X] T027 [US1] Write `UsageGuidance` in `src/AskLucy.Application/Conversations/Capabilities/SpatialAnalysisCapability.cs`: report count and resolved area, say plainly when none were found, never claim a layer was drawn when the result was a question or failure.

**Checkpoint**: US1 works end-to-end and is the MVP.

---

## Phase 4: User Story 2 — Several operations in one request (Priority: P1)

**Goal**: Independent operations run concurrently and merge into one result with distinct layers.

**Independent Test**: A two-operation request produces both layers and takes at most 75% of the sequential time.

### Tests for User Story 2

- [X] T028 [P] [US2] Unit tests for concurrent dispatch, dependency ordering and the concurrency cap in `tests/AskLucy.Application.Tests/Spatial/SpatialAnalysisCoordinatorParallelTests.cs` (use fake specialists with controlled delays).
- [X] T029 [P] [US2] Unit tests for merging multiple outcomes, de-duplication and `Partial`/`Complete` status in `tests/AskLucy.Application.Tests/Spatial/SpatialResultNormalizerTests.cs`.

### Implementation for User Story 2

- [X] T030 [US2] Extend `src/AskLucy.Application/Spatial/SpatialAnalysisCoordinator.cs` with multi-operation dispatch: dependency-aware start order, `SemaphoreSlim` cap constant, and no intermediate result presented as final.
- [X] T031 [US2] Extend `src/AskLucy.Application/Spatial/SpatialResultNormalizer.cs` to merge several outcomes, de-duplicate by stable source id, and derive `Complete`/`Partial`/`Empty`.
- [X] T032 [US2] Add `src/AskLucy.Application/Spatial/Specialists/ProximitySpecialist.cs` as the registration seam for spec 093's calculator/provider; if 093 is not yet implemented, the kind remains declared but unregistered and is reported as not performed (never silently dropped).
- [X] T033 [US2] Update the schema and Tier-2 guidance in `src/AskLucy.Application/Conversations/Capabilities/SpatialAnalysisCapability.cs` to accept and describe multiple operations, including the not-performed warning for unsupported kinds.

**Checkpoint**: US2 delivers fan-out/merge on top of US1.

---

## Phase 5: User Story 3 — Implicit location from the map (Priority: P1)

**Goal**: "here"/"this area" resolves from the current map view; explicit places win; ambiguity asks.

**Independent Test**: With a known camera and no named place, the analysis area matches the current view and the response states it.

### Tests for User Story 3

- [X] T034 [P] [US3] Unit tests for the resolution order (explicit → active boundary → viewport → ask) and invalid map context in `tests/AskLucy.Application.Tests/Spatial/StudyAreaResolverTests.cs`.

### Implementation for User Story 3

- [X] T035 [US3] Capture map context at send time from `src/AskLucy.Web/ClientApp/src/viewer/store/googleMapsStore.ts` and include it in the chat request body built in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts`.
- [X] T036 [US3] Wire `src/AskLucy.Application/Spatial/StudyAreaResolver.cs` into `src/AskLucy.Application/Spatial/SpatialAnalysisCoordinator.cs` and prefer explicitly stated locations.
- [X] T037 [US3] Enforce map-context validation server-side (`SpatialBundleValidator`) and return an actionable validation failure from `src/AskLucy.Web/Controllers/v1/AiController.cs` — never silently clamp a different area and describe it as the requested one.
- [X] T038 [US3] Update the narration guidance in `src/AskLucy.Application/Conversations/Capabilities/SpatialAnalysisCapability.cs` to state the resolved area, and to ask a focused which-area question when nothing resolves.

**Checkpoint**: US3 makes spatial requests view-aware.

---

## Phase 6: User Story 4 — User-managed layers (Priority: P2)

**Goal**: Named marker/buffer/area layers with show/hide, remove, clear, synchronized legend, and 2D/3D continuity.

**Independent Test**: A multi-layer result can be hidden, removed and cleared with the legend staying in sync in both modes.

### Tests for User Story 4

- [X] T039 [P] [US4] Component tests for legend controls and store synchronization in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayerLegend.test.tsx`.
- [X] T040 [P] [US4] Store tests for multi-kind layers, remove-one and clear-analysis in `src/AskLucy.Web/ClientApp/src/store/spatialLayersStore.test.ts`.

### Implementation for User Story 4

- [X] T041 [US4] Create `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayerLegend.tsx` with show/hide, remove-one and clear-analysis controls; render it beside the existing POI legend pattern and keep `legendLabel` synchronized.
- [X] T042 [US4] Extend `src/AskLucy.Web/ClientApp/src/features/viewer/components/SpatialLayersOverlay.tsx` to render `buffer`/`area` polygons and marker layers with the server-assigned style in 2D and 3D.
- [X] T043 [US4] Implement the keep/replace policy in `src/AskLucy.Application/Spatial/SpatialAnalysisCoordinator.cs` using `SpatialLayerStateStore`: ask before replacing another analysis's layers, honour an explicit `layerMode`, and never draw on a question path.
- [X] T044 [US4] Generalize the active-layers input from `ActivePoiCategories` to `ActiveSpatialLayers` across `SendChatMessageCommand`, `AiController`, `TurnContextFactory`, and `aiApi.ts`; reconcile client removals on the next turn.
- [X] T045 [US4] Verify 2D/3D continuity and clear-analysis behavior in `SpatialLayersOverlay.tsx` and the store; add the mode-switch case to the component test.

**Checkpoint**: US4 completes the viewer lifecycle.

---

## Phase 7: User Story 5 — Visible failures and partial results (Priority: P2)

**Goal**: One failed specialist never hides the others; all-fail is explicit; empty is not failure.

**Independent Test**: A forced single failure still delivers the successful layer with a visible warning; all-fail produces no layer event and a clear narration.

### Tests for User Story 5

- [X] T046 [P] [US5] Unit tests for failure isolation, all-fail, cancellation and provider-failure-not-empty in `tests/AskLucy.Application.Tests/Spatial/SpatialFailureTests.cs`.
- [X] T047 [P] [US5] Frontend test for the parse-failure fallback path (no unhandled rejection, narration still shown) in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.test.ts`.

### Implementation for User Story 5

- [X] T048 [US5] Add per-operation exception capture to `src/AskLucy.Application/Spatial/SpatialAnalysisCoordinator.cs`: successful and empty outcomes are kept, failed ones become warnings/coverage, and all-fail returns `AgentToolResult.Failure`.
- [X] T049 [US5] Add structured logging in `src/AskLucy.Application/Spatial/SpatialAnalysisCoordinator.cs` with `bundleId`, `analysisId`, `operationId`, `operationKind` and specialist name — no swallowed exceptions.
- [X] T050 [US5] Update the narration guidance in `src/AskLucy.Application/Conversations/Capabilities/SpatialAnalysisCapability.cs` for partial, all-fail and empty outcomes, and ensure `SpatialLayersEvent` is not emitted for a failed analysis.
- [X] T051 [US5] Harden `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts` so malformed `__SPATIAL_LAYERS__` payloads are caught and surfaced as a visible fallback, never a console-only error.

**Checkpoint**: US5 satisfies the no-silent-failure rule.

---

## Phase 8: User Story 6 — Adding a specialist without touching the core (Priority: P3)

**Goal**: Registration alone adds a capable specialist.

**Independent Test**: A test specialist is interpreted, dispatched, normalized and rendered with no edits to coordinator, capability, SSE writer or client store.

- [X] T052 [P] [US6] Extract the supported-kind declaration and interpretation wording into data in `src/AskLucy.Application/Spatial/SpatialOperationKinds.cs`, so the bundle builder reads a table rather than a switch.
- [X] T053 [US6] Add an architecture/extensibility test in `tests/AskLucy.Application.Tests/Spatial/SpatialExtensibilityTests.cs` that registers a fake specialist through DI and asserts it is dispatched and normalized without core edits.
- [X] T054 [US6] Verify the add-a-specialist steps in `specs/094-spatial-analysis-orchestrator/contracts/spatial-specialist.md` match the implementation and correct any drift.

**Checkpoint**: The extensibility claim is proven by a test.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Remove the retired path and validate the whole feature.

- [X] T055 [P] Remove the retired `PoiLayerCommand`, `PoiLayerStateStore`, `poiLayersStore.ts`, `__POI_LAYERS__` parser and obsolete tests; fix every remaining reference found by a repository-wide search.
- [X] T056 [P] Update `docs/ARCHITECTURE.md` and `docs/CONVERSATIONAL_AGENT_RUNTIME.md` with the coordinator, the bundle contract and `__SPATIAL_LAYERS__`.
- [ ] T057 Run `dotnet test` for `tests/AskLucy.Application.Tests` and `tests/AskLucy.Web.Tests`; fix regressions.
- [ ] T058 Run the full ClientApp suite and `npx tsc -b --noEmit` from `src/AskLucy.Web/ClientApp`; run the production build; fix regressions (do not run bare `tsc --noEmit`).
- [ ] T059 Execute `specs/094-spatial-analysis-orchestrator/quickstart.md` scenarios 1–8 against a running app.
- [X] T060 Security review of `src/AskLucy.Application/Spatial/SpatialBundleValidator.cs`, `src/AskLucy.Application/Spatial/StudyAreaResolver.cs`, `src/AskLucy.Application/Spatial/SpatialResultNormalizer.cs` and each specialist: map-context validation, study-area bounding, layer attribute allowlist, and authorization on every provider call.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup; blocks every user story.
- **US1 (Phase 3)**: Depends on Foundational. MVP.
- **US2 (Phase 4)**: Depends on US1 (extends the coordinator and normalizer).
- **US3 (Phase 5)**: Depends on Foundational; integrates with US1's coordinator.
- **US4 (Phase 6)**: Depends on US1 (store/overlay exist in marker form).
- **US5 (Phase 7)**: Depends on US1 and US2 (multi-outcome paths).
- **US6 (Phase 8)**: Depends on US1 (registration seam) and US2 (multi-kind dispatch).
- **Polish (Phase 9)**: Depends on all delivered stories.

### User Story Dependencies

- US1 is the only story with no story dependency.
- US2, US3, US4, US5, US6 can start in parallel once US1's coordinator and capability exist (US2/US5 share files — sequence those two).

### Within Each Story

- Tests first (write, confirm failure).
- Models/contracts before services; services before capability/capability wiring; backend before client integration.
- One file per task wherever possible to keep [P] safe.

### Parallel Opportunities

- T004–T006, T008–T010, T012–T015 are independent file sets.
- Within US1, T017–T020 (tests) run in parallel.
- US3, US4 and US6 can proceed in parallel with US2 if capacity allows; US5 follows US2.

---

## Parallel Example: User Story 1

```text
T017  Bundle validator + registry tests        (tests/.../SpatialBundleValidatorTests.cs)
T018  POI specialist tests                      (tests/.../PoiDiscoverySpecialistTests.cs)
T019  Stream-event contract test                (tests/AskLucy.Web.Tests/Spatial/SpatialLayersEventTests.cs)
T020  Store + SSE parsing tests                 (ClientApp/src/store/spatialLayersStore.test.ts)
```

## Parallel Example: User Story 4

```text
T039  Legend component tests                    (ClientApp/.../SpatialLayerLegend.test.tsx)
T040  Multi-kind store tests                    (ClientApp/src/store/spatialLayersStore.test.ts)
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Phase 1 → Phase 2 → Phase 3 (US1).
2. **STOP and VALIDATE**: "Find hospitals in Muscat" renders through `__SPATIAL_LAYERS__` and the old path is gone.
3. Demo the single front door.

### Incremental Delivery

1. US1: one coordinated POI answer.
2. US2: multi-operation fan-out/merge.
3. US3: map-context awareness.
4. US4: full layer lifecycle.
5. US5: failure isolation and visibility.
6. US6: extensibility proof.

Each increment is independently testable and does not break earlier stories.

---

## Notes

- [P] tasks touch different files and have no incomplete dependency.
- Do not keep the old POI path "just in case": FR-001 requires exactly one front door.
- Do not add persistence, a hub, a REST spatial endpoint, or a second layer event.
- Run the full ClientApp suite, not just touched files: `ChatPage.test.tsx` holds independent assertions.
- Commit after each logical group.
