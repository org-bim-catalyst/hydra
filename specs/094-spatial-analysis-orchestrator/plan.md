# Implementation Plan: Spatial Request Bundle & Analysis Orchestrator

**Branch**: `094-spatial-analysis-orchestrator` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/094-spatial-analysis-orchestrator/spec.md`

## Summary

Introduce one spatial front door. A new `SpatialAnalysisCapability` (the coordinator) replaces the bespoke
per-capability spatial paths: it interprets a natural-language request into a versioned `SpatialRequestBundle`,
dispatches each operation to a registered `ISpatialSpecialist` concurrently, merges the specialist results into
one normalized `SpatialAnalysisResult`, and delivers named viewer layers through a single stream event.

The existing POI discovery path — `FindPointsOfInterestCapability` → `IPoiQueryProvider` → `PoiLayerCommand` →
`__POI_LAYERS__` → `poiLayersStore` → `PoiLayersOverlay` — becomes the first specialist behind that coordinator.
Proximity analysis (spec 093) registers through the same seam. Map context (centre, zoom, extent, active
boundary) is carried on the chat command and used to resolve implicit locations.

Everything is in-turn and session-scoped: no new background job, hub, aggregate, table, or migration. The
coordinator is a conversation capability like today's POI capability, so results stream back in the same turn
and the client keeps owning layer state.

## Technical Context

**Language/Version**: C# / .NET 10 (backend), TypeScript 5 + React 19 (frontend)

**Primary Dependencies**: Existing MediatR/CQRS, FluentValidation, `IConversationCapability`/`AgentToolCatalog`,
`ILocationResolutionService`, `IPoiQueryProvider`/`OverpassPoiQueryProvider`, `PoiCategoryCatalog`,
`GeometryMath`, `ChatStreamChunk` trailing-event channel, Zustand stores, MUI. No new package.

**Storage**: None new. Layer state stays in-process and session-scoped, generalizing `PoiLayerStateStore`.
Spatial feature data remains in the existing providers.

**Testing**: xUnit + NSubstitute (Application/Web tests), Vitest + Testing Library (ClientApp), architecture
tests for layer boundaries.

**Target Platform**: Existing ASP.NET Core web application and browser SPA.

**Project Type**: Web application — Clean Architecture backend plus React SPA under `src/AskLucy.Web/ClientApp`.

**Performance Goals**: SC-002 — at least 95% of successful unambiguous requests render within 10 s, excluding
clarification; SC-003 — two independent operations complete materially faster than the sum of their sequential
durations (asserted behaviourally, not by a latency harness).

**Constraints**: In-turn execution only; no new background job, SignalR hub, persistence, or REST spatial
endpoint. Existing authorization, rate limits and provider quotas apply per specialist. Map context is
untrusted input and must be validated. No silent failures.

**Scale/Scope**: Per-user, per-conversation, one coordinator with 1 migrated specialist (POI discovery) plus a
registration seam for proximity (spec 093). Two-layer viewer representations used in this release: marker
layers (existing) and area/buffer layers (new). Designed for any number of specialists.

**Unknowns**: None outstanding. The workflow-engine question was resolved against the current code: its
`Parallel`/`Merge` fan-out is built for durable background executions and batches node notifications until every
branch settles (057 research D3), which cannot serve in-turn streaming; see research D1.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Assessment |
|---|---|---|
| §2 I | Clean Architecture / Dependency Rule | **PASS.** Bundle, specialist contract, coordinator, normalization and layer command are Application-owned; `OverpassPoiQueryProvider` and any future specialist provider stay in Infrastructure. The client receives a command record, never an infrastructure type. |
| §2 II | SOLID | **PASS.** Each specialist is a new class implementing a narrow interface (OCP). The registry depends on `IEnumerable<ISpatialSpecialist>`; adding a specialist edits no existing class. The specialist interface is one method (ISP). |
| §2 III | DRY / KISS / YAGNI | **PASS.** Reuses location resolution, the POI provider and catalog, `GeometryMath`, the `PoiLayerStateStore` shape and the existing trailing-event channel. No new datastore, queue, or abstraction without a current requirement. `SpatialRequestBundle` is required by FR-003, not speculative. |
| §2 IV | Composition over inheritance | **PASS.** Specialists compose via interface; no inheritance introduced. |
| §2 V | Dependency inversion & testability | **PASS.** The coordinator takes injected `ISpatialSpecialist` set, `ILocationResolutionService` and abstractions; unit-testable with fakes, no network or database. |
| §2 VI | Separation of concerns | **PASS.** Interpretation/merge/delivery live in the coordinator; retrieval lives in specialists/providers; rendering lives in the client. |
| §2 VII | Convention over configuration | **PASS.** New feature folder `src/AskLucy.Application/Spatial/`, DI registration beside the existing capability registrations, viewer extension contributed by the existing `poiMarkerExtension` pattern. |
| §2 VIII | No Silent Failures (NON-NEGOTIABLE) | **PASS.** Specialist exceptions are captured per operation, surfaced in the merged result's warnings/status, and logged with request/operation/specialist identifiers. All-fail returns `AgentToolResult.Failure`; no empty-success path. Client parsing failures fall back to no layer and the narration still reports the outcome. |
| §3 | Architecture rules (CQRS, repositories, DI) | **PASS.** The coordinator is a capability (application command path) and persists nothing; no repository or `IQueryable` changes. |
| §5 | Database | **PASS.** No schema change and no migration. Layer state is explicitly session-scoped view state. |
| §6 | API standards | **PASS.** No new endpoint. The existing authenticated chat stream carries one additional trailing event, following the `__LOCATION__`/`__POI_LAYERS__` convention. |
| §7 | UI principles | **PASS.** Reuses the existing overlay/extension contribution mechanism and MUI legend; layer controls keep keyboard/a11y behaviour inherited from the current legend. |
| §8 | Security | **PASS.** Map context is validated server-side; study areas are bounded; provider authorization is unchanged; specialist output is treated as untrusted data and validated before mapping to a layer command. |
| §9 | AI principles | **PASS.** Provider-neutral: specialists use existing abstractions; no vendor is named in the coordinator. |
| §10 | Testing | **PASS.** Unit tests for bundle versioning/validation, registry dispatch, concurrency, merge/failure isolation, context resolution; contract test for the trailing event; frontend tests for store and overlay; behavioural concurrency assertion for SC-003. |
| §13 | Documentation | **PASS.** Spec set plus contracts under this directory; no ADR required because no new architecture dependency is introduced (the coordinator pattern already exists in 057). |

**Gate result: PASS.**

**Post-Phase 1 re-check: PASS.** The data model introduced no persistence or cross-layer reference; contracts
kept validation server-side; the bundle remains Application-owned.

## Project Structure

### Documentation (this feature)

```text
specs/094-spatial-analysis-orchestrator/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── spatial-request-bundle.md
│   ├── spatial-specialist.md
│   └── spatial-layers-event.md
├── checklists/
│   └── requirements.md
└── tasks.md                 # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── AskLucy.Application/
│   ├── Spatial/                              # NEW feature folder
│   │   ├── SpatialRequestBundle.cs           # versioned bundle + operation + selector + map context
│   │   ├── SpatialAnalysisResult.cs          # normalized result, status, coverage, warnings
│   │   ├── SpatialLayer.cs                   # layer kind/geometry/style/legend contract
│   │   ├── ISpatialSpecialist.cs             # specialist contract
│   │   ├── SpatialSpecialistRegistry.cs      # operation-kind → specialist lookup
│   │   ├── SpatialAnalysisCoordinator.cs     # interpret → validate → dispatch → merge
│   │   ├── SpatialBundleValidator.cs         # FluentValidation rules
│   │   ├── SpatialResultNormalizer.cs        # specialist → normalized result/layers
│   │   ├── SpatialLayerStateStore.cs         # generalized PoiLayerStateStore (pending question + layers)
│   │   ├── StudyAreaResolver.cs              # implicit location via map context / active boundary / geocoding
│   │   └── Specialists/
│   │       ├── PoiDiscoverySpecialist.cs     # wraps PoiCategoryCatalog + IPoiQueryProvider
│   │       └── ProximitySpecialist.cs        # registration seam; implemented with spec 093
│   ├── Conversations/Capabilities/
│   │   ├── SpatialAnalysisCapability.cs      # NEW — the single front door (IConversationCapability)
│   │   ├── FindPointsOfInterestCapability.cs # RETIRED (replaced by the coordinator + POI specialist)
│   │   └── IConversationCapability.cs        # MODIFIED — SubAgentArea.Spatial (or reuse Location)
│   ├── Viewer/
│   │   └── SpatialLayerCommand.cs            # NEW — replaces/extents PoiLayerCommand
│   ├── Ai/Commands/SendChatMessage/
│   │   ├── ChatStreamChunk.cs                # MODIFIED — SpatialLayers replaces PoiLayer
│   │   └── SendChatMessageCommand.cs         # MODIFIED — MapViewportContext
│   └── Conversations/
│       ├── Capabilities/TurnContext.cs       # MODIFIED — MapViewportContext
│       ├── Capabilities/TurnContextFactory.cs# MODIFIED — map context from the command
│       └── Runtime/StructuredPayloadExtractor.cs # MODIFIED — coordinate result → layer event
├── AskLucy.Infrastructure/
│   └── Overpass/OverpassPoiQueryProvider.cs  # UNCHANGED — reused by the POI specialist
└── AskLucy.Web/
    ├── Controllers/v1/AiController.cs        # MODIFIED — __SPATIAL_LAYERS__ writer
    └── ClientApp/src/
        ├── features/chat/api/aiApi.ts        # MODIFIED — parse __SPATIAL_LAYERS__
        ├── features/chat/hooks/useChatStream.ts
        ├── store/spatialLayersStore.ts       # NEW — replaces poiLayersStore
        ├── features/viewer/components/SpatialLayersOverlay.tsx
        ├── features/viewer/components/SpatialLayerLegend.tsx
        └── viewer/extensions/builtin/poiMarkerExtension.tsx # MODIFIED — contributes the new overlay
```

**Structure Decision**: Standard Clean Architecture layering, organized by feature within each layer
(constitution §4). The coordinator is a conversation capability because spatial requests are in-turn and their
results stream back through the existing chat channel; this is the only path that can satisfy FR-022 and the
existing viewer UX without introducing background infrastructure.

## Key Implementation Notes

1. **Retire, don't duplicate.** `FindPointsOfInterestCapability` must be removed from DI once
   `SpatialAnalysisCapability` is registered; leaving both would give the decider two spatial front doors and
   violate FR-001. The POI specialist keeps the category resolution, keep/replace semantics and provider-error
   mapping; the coordinator takes over area/location resolution and delivery.
2. **Keep the pending-question semantics.** `PoiLayerStateStore` already models "keep or replace?" and "which
   area?" with a 10-minute expiry. Generalize it rather than rewriting: rename to `SpatialLayerStateStore`, key
   layers by analysis + kind, and keep the pending-question model. Losing this would regress specs 090's UX.
3. **Do not use the workflow engine for this fan-out.** See research D1: `ExecuteParallelAsync` batches node
   notifications and is built for durable background runs; the coordinator needs in-turn results, and spec
   Assumptions exclude a background job/push channel.
4. **One event, not two.** Emit `__SPATIAL_LAYERS__` carrying an array of layers. The POI marker layer is one
   kind; proximity buffers/areas are others. Do not add a second event alongside `__POI_LAYERS__` — a second
   path is exactly what this feature removes.
5. **Validate before the viewer.** Specialist output is model- and provider-influenced; validate geometry
   shape, coordinate ranges, layer kinds and attribute allowlists before building a `SpatialLayerCommand`.
6. **Map context validation.** Reject non-finite coordinates, zoom outside the supported range, inverted bounds,
   and study areas above the existing provider limits with user-visible explanations — never silently clamp to
   a different area and report it as the requested one.
7. **Frontend error path.** `__SPATIAL_LAYERS__` parsing errors must not become unhandled rejections; the turn
   outcome still narrates the result, and the layer is omitted with a visible fallback state.

## Complexity Tracking

No constitution violations. One deliberate design deviation is recorded for reviewers:

| Deviation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| In-process `Task.WhenAll` dispatch instead of the workflow engine's `Parallel`/`Merge` nodes | Results must stream back in the same conversational turn; the workflow runner is a background/Hangfire path and its parallel notifications are batched until all branches settle (057 research D3) | Workflow fan-out would delay results to after the turn, require a new hub/notification channel and contradict spec 093's resolved integration decision; the Coordinator→Specialist contract and merge logic are the reusable part, not the engine |
