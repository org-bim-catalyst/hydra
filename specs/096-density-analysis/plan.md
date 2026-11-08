# Implementation Plan: Density Analysis

**Branch**: `096-density-analysis` | **Date**: 2026-11-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/096-density-analysis/spec.md`

## Summary

Add `density.analysis` as a registered Spatial Analysis specialist. The conversation capability builds a versioned bundle operation with a resolved dataset, bounded study area, method, effective parameters, and display preferences. `DensityAnalysisSpecialist` validates it, calls an Application-owned provider to calculate point-count, point-kernel, or population density, classifies values, audits execution, and returns `density-surface` layer drafts through the existing coordinator and chat stream. No new controller, persistence table, viewer, or parallel orchestration path is introduced.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript 5 / React 19.

**Primary Dependencies**: Existing `ISpatialSpecialist`, `SpatialRequestBundle`, coordinator, normalizer, FluentValidation, MediatR, Serilog, React, Zustand, Material UI, Google Maps integration, and chat stream. No new package.

**Storage**: No new persistent storage. Results and visualization settings are session-scoped; append-only audit records use the structured-log sink.

**Testing**: xUnit + NSubstitute; Vitest + Testing Library; fixed spatial fixtures for deterministic value and class-boundary tests.

**Target Platform**: ASP.NET Core web application with React SPA in `src/AskLucy.Web/ClientApp`.

**Project Type**: Clean Architecture web application.

**Performance Goals**: 95% of normal-area successful analyses become visible with a legend and summary within 10 seconds; operations obey the existing 30-second deadline.

**Constraints**:

- A resolved bounded study area is required.
- Point methods accept catalogued point datasets; population density requires catalogued population-with-area data.
- Parameters and calculated values are finite, validated, bounded, and disclosed after defaulting.
- Style changes never mutate source values; calculation changes create a new result/audit entry.
- Every failure is visible and actionable, never console-only or log-only.

**Scale/Scope**: Initial eligible sources are registered POI datasets (buildings, schools, hospitals, etc.) plus valid population-area datasets. No population estimation or coverage invention.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| Clean Architecture | **PASS.** Application owns density definitions, catalog, provider interface, specialist, and validation; Infrastructure implementations depend inward. |
| SOLID / DI / testability | **PASS.** One specialist and narrow provider/catalog interfaces are constructor-injected and fakeable. |
| DRY / KISS / YAGNI | **PASS.** Reuses the established bundle, coordinator, layer event/store, viewer lifecycle, audit pattern, and error model. |
| Separation of concerns | **PASS.** Application interprets/validates, Infrastructure calculates/retrieves, and client renders/manages layers. |
| No silent failures | **PASS.** Explicit specialist outcomes plus shared chat-payload and map-render error feedback replace console-only paths. |
| Security / provider neutrality | **PASS.** Existing authorization, study-area limits, rate limits, and provider abstraction apply; no vendor type leaks outward. |
| Testing / documentation | **PASS.** Fixture, unit, provider, stream, store, overlay, legend, contract, and quickstart validation are included. |

**Gate result before research: PASS.**

**Post-Phase 1 re-check: PASS.** No new outward dependency, persistent model, API endpoint, or orchestration path is required. Density surface transport is an additive, validated layer contract.

## Project Structure

### Documentation (this feature)

```text
specs/096-density-analysis/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── density-operation-parameters.md
│   ├── density-analysis-provider.md
│   └── density-surface-layer-event.md
├── checklists/requirements.md
└── tasks.md                         # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── AskLucy.Application/
│   ├── Conversations/Capabilities/SpatialAnalysisCapability.cs # EXTEND: density parsing/schema
│   ├── Spatial/
│   │   ├── SpatialRequestBundle.cs                             # EXTEND: kind/parameters
│   │   ├── SpatialBundleValidator.cs                           # EXTEND: validation
│   │   ├── SpatialAnalysisResult.cs                            # EXTEND: density-surface
│   │   ├── Specialists/DensityAnalysisSpecialist.cs            # NEW
│   │   └── Density/                                            # NEW: contracts/policy/audit
│   └── DependencyInjection.cs                                  # EXTEND
├── AskLucy.Infrastructure/
│   ├── Spatial/Density/OverpassDensityAnalysisProvider.cs      # NEW
│   ├── Spatial/Density/StaticPopulationDensityProvider.cs      # NEW: unavailable/static seam
│   └── DependencyInjection.cs                                  # EXTEND
└── AskLucy.Web/ClientApp/src/
    ├── store/spatialLayersStore.ts                             # EXTEND
    ├── features/chat/api/aiApi.ts                              # EXTEND: visible malformed payload error
    ├── features/chat/hooks/useChatStream.ts                    # EXTEND
    └── features/viewer/components/
        ├── SpatialLayersOverlay.tsx                            # EXTEND: renderer/error feedback
        └── SpatialLayerLegend.tsx                              # EXTEND: legend/controls

tests/
├── AskLucy.Application.Tests/Spatial/Density/                  # NEW: specialist, validator, classes, audit
├── AskLucy.Infrastructure.Tests/Spatial/Density/               # NEW: provider fixtures
└── AskLucy.Web/ClientApp/src/                                  # EXTEND: store, overlay, legend tests
```

**Structure Decision**: Extend the existing spatial vertical slice. Application owns contracts and orchestration, Infrastructure owns source adapters, and the current layer store/viewer remains the sole client integration point.

## Complexity Tracking

No constitution violations or justified complexity exceptions.
