# Implementation Plan: Spatial Query Analysis

**Branch**: `095-spatial-query-analysis` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/095-spatial-query-analysis/spec.md`

---

## Summary

Add `spatial.query` as a new registered specialist within the existing Spatial Analysis Orchestrator (spec 094).
When a user asks a natural-language question that filters geographic features by attribute values, spatial
relationships, or both, the coordinator builds a `SpatialRequestBundle` containing a `spatial.query` operation,
dispatches it to the new `SpatialQuerySpecialist`, which resolves the target dataset, translates predicates into
a `SpatialQueryDefinition`, executes the query against an `ISpatialQueryProvider`, and streams paginated
`QueryResultFeature` batches back to the coordinator. The coordinator normalizes the result into named
`SpatialLayer`s of kind `feature-query` (new layer kind) and emits a single `__SPATIAL_LAYERS__` trailing event.

The existing `spatialLayersStore` is extended to handle paginated batch arrivals and feature-level attribute
display. No new persistence, hub, or REST endpoint is introduced.

---

## Technical Context

**Language/Version**: C# / .NET 10 (backend) · TypeScript 5 + React 19 (frontend)

**Primary Dependencies**:
- Backend: existing `ISpatialSpecialist` / `SpatialRequestBundle` / `SpatialOperationKinds` / `SpatialAnalysisResult` / `SpatialLayer` (spec 094); `IPoiQueryProvider` for data-source patterns; `FluentValidation`; `MediatR`; `System.Text.Json`; `Serilog`. No new NuGet package.
- Frontend: `useSpatialLayersStore` (Zustand); `SpatialLayersOverlay.tsx`; `SpatialLayerLegend.tsx`; MUI. No new npm package.

**Storage**: None new. Query results are transient, session-scoped layer state. Audit log entries are written
to the existing structured logging sink (Serilog). No migration.

**Testing**: xUnit + NSubstitute (Application unit tests); Vitest + Testing Library (frontend); existing
architecture test suite for layer boundary enforcement.

**Target Platform**: Existing ASP.NET Core web application + React SPA under `src/AskLucy.Web/ClientApp`.

**Project Type**: Web application — Clean Architecture backend + React SPA.

**Performance Goals**:
- SC-001: first feature batch visible within 3 seconds of query submission.
- Overall query execution must complete or be cancelled within 30 seconds.
- SC-005: attribute panel opens within 2 seconds of feature selection.

**Constraints**:
- In-turn execution only; same dispatch pattern as specs 093–094 (`Task.WhenAll`, no Hangfire job).
- Results streamed in paginated batches (default batch size: 200 features) via the existing `__SPATIAL_LAYERS__` trailing-event channel with an extended `batchIndex` / `totalCount` envelope.
- Authentication required; all datasets accessible to any authenticated user (clarification Q3).
- AND across predicate types; OR within predicate type only (clarification Q4).
- Full audit log per query (clarification Q5) written via Serilog structured log.
- No hard result cap; results stream until exhausted or the 30-second timeout fires.

**Scale/Scope**: Initial Dataset Catalog: land parcels, buildings, roads, development areas. Catalog is
extensible by registration; no code changes to the query engine needed to add a dataset.

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Assessment |
|---|-----------|------------|
| §2 I | Clean Architecture / Dependency Rule | **PASS.** `SpatialQueryDefinition`, `ISpatialQueryProvider`, `IDatasetCatalog`, `AttributePredicate`, `SpatialPredicate`, `SpatialQuerySpecialist` all live in Application. Infrastructure adapters depend inward. The client receives a layer command, never an infrastructure type. |
| §2 II | SOLID | **PASS.** `SpatialQuerySpecialist` is one new class behind `ISpatialSpecialist` (OCP). `ISpatialQueryProvider` is a new narrow interface (ISP). Each `IPredicateTranslator` handles one predicate kind (SRP). |
| §2 III | DRY / KISS / YAGNI | **PASS.** Reuses `ISpatialSpecialist`, `SpatialRequestBundle`, `SpatialLayer`, `spatialLayersStore`, `__SPATIAL_LAYERS__` event channel, and `FeatureSelector`. No new abstraction without a current requirement. |
| §2 IV | Composition over inheritance | **PASS.** `IPredicateTranslator` implementations compose by injection; no inheritance hierarchy. |
| §2 V | Dependency inversion & testability | **PASS.** `SpatialQuerySpecialist` takes `ISpatialQueryProvider`, `IDatasetCatalog`, `IPredicateTranslator[]` by constructor injection; unit-testable with fakes, no network or DB needed. |
| §2 VI | Separation of concerns | **PASS.** Predicate extraction in Application; dataset retrieval and filter execution in Infrastructure adapters; rendering in the client. The specialist never narrates or touches the viewer. |
| §2 VII | Convention over configuration | **PASS.** `spatial.query` declared in `SpatialOperationKinds.Supported`. Registered in `DependencyInjection.cs` beside existing specialists. |
| §2 VIII | No Silent Failures (NON-NEGOTIABLE) | **PASS.** Provider exceptions → `Failed` outcome with reason; timeout → query cancelled cleanly, user notified; partial batches labeled; empty result explicitly reported; no uncaught async paths on frontend or backend. |
| §3 | Architecture rules (CQRS, DI) | **PASS.** No repository or `IQueryable` change; specialist is a pure application-layer component. |
| §5 | Database | **PASS.** No schema change; no migration. Audit entries are structured log lines. |
| §6 | API standards | **PASS.** No new endpoint. The existing authenticated chat stream carries extended `__SPATIAL_LAYERS__` trailing events. |
| §7 | UI principles | **PASS.** `SpatialLayersOverlay` and `SpatialLayerLegend` extended minimally. `FeatureAttributePanel` follows existing MUI panel patterns. Keyboard/a11y handled by MUI defaults. |
| §8 | Security | **PASS.** Authentication enforced on all requests. Dataset names and predicate values validated and sanitised before execution; provider calls are user-scoped. |
| §9 | AI principles | **PASS.** Provider-neutral: `ISpatialQueryProvider` abstracts data retrieval; no AI vendor named in the specialist. |
| §10 | Testing | **PASS.** Unit tests for predicate parsing, query definition validation, provider dispatch, batch streaming, timeout cancellation, audit logging, frontend store batch handling, and attribute panel rendering. |
| §13 | Documentation | **PASS.** Spec + plan + contracts documented here; no ADR required (follows the registration-only extension pattern already justified in spec 094). |

**Gate result: PASS.**

**Post-Phase 1 re-check: PASS.** Data model introduced no persistence or cross-layer reference; contracts kept
validation server-side; `SpatialQueryDefinition` remains Application-owned.

---

## Project Structure

### Documentation (this feature)

```text
specs/095-spatial-query-analysis/
├── spec.md
├── plan.md                  # this file
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── query-operation-parameters.md
│   ├── spatial-query-provider.md
│   └── feature-query-layer-event.md
├── checklists/
│   └── requirements.md
└── tasks.md                 # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── AskLucy.Application/
│   ├── Spatial/
│   │   ├── SpatialRequestBundle.cs             # EXTEND: add SpatialQueryParameters block
│   │   ├── SpatialOperationKinds.cs            # EXTEND: add "spatial.query" to Supported list
│   │   ├── SpatialAnalysisResult.cs            # EXTEND: add SpatialLayerKind.FeatureQuery
│   │   ├── Specialists/
│   │   │   ├── PoiDiscoverySpecialist.cs       # unchanged
│   │   │   ├── ProximityAnalysisSpecialist.cs  # unchanged
│   │   │   └── SpatialQuerySpecialist.cs       # NEW
│   │   └── Query/                              # NEW sub-folder
│   │       ├── IDatasetCatalog.cs              # NEW: dataset registry abstraction
│   │       ├── DatasetCatalog.cs               # NEW: in-memory catalog + registration
│   │       ├── DatasetEntry.cs                 # NEW: dataset descriptor record
│   │       ├── AttributeField.cs               # NEW: field descriptor (name, type, unit)
│   │       ├── AttributePredicate.cs           # NEW: field + operator + value + unit
│   │       ├── SpatialPredicate.cs             # NEW: relationship + reference + distance
│   │       ├── PredicateGroup.cs               # NEW: within-type AND/OR combinator
│   │       ├── SpatialQueryDefinition.cs       # NEW: validated, typed query
│   │       ├── ISpatialQueryProvider.cs        # NEW: execution abstraction
│   │       ├── QueryResultFeature.cs           # NEW: one returned feature with attributes
│   │       └── SpatialQueryAuditLogger.cs      # NEW: structured audit log writer
│
├── AskLucy.Infrastructure/
│   └── Spatial/
│       └── Query/                              # NEW sub-folder
│           ├── OverpassSpatialQueryProvider.cs # NEW: OSM-backed provider for POI-type datasets
│           └── StaticDatasetQueryProvider.cs   # NEW: placeholder for cadastral/parcels/open-data
│
└── AskLucy.Web/
    ├── ClientApp/src/
    │   ├── store/
    │   │   └── spatialLayersStore.ts           # EXTEND: batch arrivals, totalCount, feature attributes
    │   ├── features/siteAnalysis/
    │   │   ├── SpatialLayersOverlay.tsx        # EXTEND: render feature-query layer kind
    │   │   ├── SpatialLayerLegend.tsx          # EXTEND: result count, batch progress indicator
    │   │   └── FeatureAttributePanel.tsx       # NEW: attribute display on feature selection
    │   └── viewer/extensions/
    │       └── spatialQueryExtension.ts        # NEW: map click → open FeatureAttributePanel

tests/
├── AskLucy.Application.Tests/
│   └── Spatial/Query/
│       ├── SpatialQuerySpecialistTests.cs
│       ├── DatasetCatalogTests.cs
│       ├── AttributePredicateTests.cs
│       ├── SpatialPredicateTests.cs
│       └── SpatialQueryAuditLoggerTests.cs
└── ClientApp/ (Vitest)
    ├── store/spatialLayersStore.batch.test.ts
    └── features/FeatureAttributePanel.test.tsx
```

**Structure Decision**: Extend the existing `Spatial/` feature folder with a `Query/` sub-folder in both
Application and Infrastructure. No new project or assembly. The specialist registers in `DependencyInjection.cs`
beside `PoiDiscoverySpecialist` and `ProximityAnalysisSpecialist`, requiring zero changes to the coordinator,
normalizer, or viewer layer architecture (the FR-028 proof from spec 094 holds).

---

## Complexity Tracking

No constitution violations. No new architectural dependency introduced.
