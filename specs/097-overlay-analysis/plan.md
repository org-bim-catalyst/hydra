# Implementation Plan: Overlay Analysis

**Branch**: `[097-overlay-analysis]` | **Date**: 2026-11-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/097-overlay-analysis/spec.md`

## Summary

Register `overlay.analysis` as a spatial specialist under the existing Spatial Analysis Agent. Lucy will interpret user criteria into typed, bounded operations in the current versioned bundle; the Application layer owns the overlay contracts and validation; an Infrastructure provider will reuse the existing spatial dataset catalog/query sources and NetTopologySuite (already referenced by Infrastructure) for supported geometry operations; normalized result features will flow through the established chat layer event and current 2D/3D layer store. Do not add a standalone controller route, result persistence, or a second layer manager. Optional scoring runs only after hard eligibility filtering and records its normalization, direction, and weights.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript 5 / React 19.

**Primary Dependencies**: Existing Spatial Analysis Agent, versioned `SpatialRequestBundle`, `ISpatialSpecialist`, `ISpatialQueryProvider` and dataset catalog, NetTopologySuite in Infrastructure, existing SSE result-layer contract, Zustand spatial layer store, Google Maps 2D overlay and current 3D viewer integration. No new package is planned.

**Storage**: No new database schema. Request-scoped result features and layers use the current chat stream and client-owned layer state; execution audit uses the existing structured logging/audit approach.

**Testing**: xUnit with fixed geometric fixtures and fake providers; Vitest/Testing Library for request/result contracts, layer state, inspection, and rendering behavior. Test geometry operation semantics, CRS handling, criteria and scoring deterministically.

**Target Platform**: ASP.NET Core web application with React SPA in `src/AskLucy.Web/ClientApp`.

**Project Type**: Clean Architecture web application.

**Performance Goals**: At least 95% of successful overlays over agreed normal study areas show a first useful result and status summary within 10 seconds; reuse the coordinator's bounded cancellation behavior and never block independent bundle operations behind one another.

**Constraints**:

- Execute only within a validated bounded study area and under existing authentication, data access, and rate constraints.
- Use only catalogued input layers and operations whose geometry, attributes, CRS and units are supported.
- Keep business rules and overlay contracts in Application; keep geometry engine and source retrieval in Infrastructure.
- Preserve source identity and per-operation lineage; distinguish empty, partial, failed and cancelled outcomes.
- Treat hard eligibility separately from optional weighted ranking; disclose score direction, normalization, and weights.
- Surface operation, validation and rendering failures visibly; do not discard failed promises or return partial data as complete.
- Reuse existing polygon/line/point feature geometry and the current viewer lifecycle. Extend the current normalized spatial feature/layer payload additively for overlay metadata only where required.

**Scale/Scope**: Initial support is bounded to existing registered spatial datasets and their declared attributes/geometries. No new external data source, arbitrary user-uploaded layer ingestion, persistent analysis project, or novel renderer is part of this feature.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| Clean Architecture and dependency rule | **PASS.** Application owns typed definitions, validation, specialist and provider abstractions; Infrastructure performs retrieval and geometry operations; Presentation carries existing contracts. |
| SOLID, DI and testability | **PASS.** A narrow overlay provider/catalog seam is injected and can be replaced with deterministic test fakes. |
| DRY, KISS and YAGNI | **PASS.** Reuse bundle dispatch, query catalog/providers, NTS already present, existing layer and error lifecycle; no new route, datastore or viewer subsystem. |
| Separation of concerns | **PASS.** Interpretation/validation, execution, result normalization and visualization remain in their existing layers. |
| No silent failures | **PASS.** Explicit statuses and visible chat/map errors cover unsupported data, partial coverage, empty results, cancellation and rendering failure. |
| Security and data custody | **PASS.** Authenticated current request, validated study extent, catalog restrictions, and existing rate/data policy remain in force. |
| Testing and documentation | **PASS.** Fixed geometry tests, provider contract tests, viewer/store tests, and this feature's data/contract/quickstart docs cover the observable behavior. |

**Gate result before research: PASS.**

**Post-Phase 1 re-check: PASS.** No new external endpoint, persistence model, or orchestration mechanism is required. Geometry output extends the established spatial feature contract and remains session-scoped.

## Research Summary

- Use a registered `overlay.analysis` specialist and the current bundle version; do not invent a direct overlay endpoint.
- Use existing catalog-backed datasets and `ISpatialQueryProvider`-style retrieval. Infrastructure can use the repository's existing NetTopologySuite package for valid geometry operations; unsupported or untransformable geometry/CRS combinations fail explicitly.
- Preserve polygon, line, and point output using the existing `FeatureGeometry` representation. Use the existing feature-query map renderer and inspector, with additive overlay metadata for operation, criterion result, score, rank and provenance.
- Evaluate hard constraints before scoring. Ranking calculations use explicit normalized criteria/direction/weights and deterministic tie-breaking; equal weights are allowed only when all criteria are comparable and disclose the default.
- Reuse current request-scoped result events and client layer lifecycle; no database migration or layer API is needed.

## Project Structure

### Documentation (this feature)

```text
specs/097-overlay-analysis/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── overlay-analysis.contract.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/AskLucy.Application/
├── Conversations/Capabilities/SpatialAnalysisCapability.cs  # extend schema, interpretation and typed input
├── Spatial/SpatialRequestBundle.cs                          # add operation kind and overlay definition
├── Spatial/SpatialBundleValidator.cs                        # validate dependencies, inputs and constraints
├── Spatial/SpatialAnalysisResult.cs                         # add overlay result metadata compatibly
├── Spatial/Specialists/OverlayAnalysisSpecialist.cs          # new coordinator specialist
├── Spatial/Overlay/                                   # new contracts, catalog policy, score rules, audit
└── DependencyInjection.cs                              # register specialist and abstractions
src/AskLucy.Infrastructure/
├── Spatial/Overlay/OverlayAnalysisProvider.cs                # new NTS-backed operation execution
└── DependencyInjection.cs                              # register provider
src/AskLucy.Web/ClientApp/src/
├── store/spatialLayersStore.ts                               # validate and manage overlay feature metadata
├── features/viewer/components/SpatialLayersOverlay.tsx       # reuse polygon/line/point result rendering
└── features/viewer/components/SpatialLayerLegend.tsx         # display operation/criteria/score meaning
tests/
├── AskLucy.Application.Tests/Spatial/Overlay/                # typed validation, criteria, specialist
├── AskLucy.Infrastructure.Tests/Spatial/Overlay/              # geometric/provider fixtures
└── AskLucy.Web/ClientApp/src/                                 # layer, render, legend and inspection tests
```

**Structure Decision**: Extend the existing spatial vertical slice. Application defines business contracts; Infrastructure retrieves and combines catalogued data; the existing coordinator and viewer deliver and manage the result.

## Complexity Tracking

No constitution violations or justified complexity exceptions.
