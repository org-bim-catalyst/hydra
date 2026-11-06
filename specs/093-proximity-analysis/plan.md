# Implementation Plan: Proximity Analysis

**Branch**: `093-proximity-analysis` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/093-proximity-analysis/spec.md`

## Summary

Add proximity operations to Lucy's existing spatial request and agent flow. Resolve natural-language requests into validated feature categories, relationship, distance bands, and bounded study area; retrieve eligible spatial data; perform accurate distance/buffer calculations; return provenance-bearing structured results; and render/manage result layers through the existing viewer extension model. No new persistence is needed for initial viewer-session layer state.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript / React 19
**Primary Dependencies**: Existing ASP.NET Core API, MediatR/CQRS, Spatial Request Bundle, agent orchestration, spatial data providers, and viewer extension/layer stores; no new dependency assumed.
**Storage**: Existing spatial data sources; transient analysis result and layer state for the active viewer session; no new database schema expected.
**Testing**: Existing .NET unit/integration tests and frontend Vitest/Testing Library suites; contract and end-to-end tests for spatial request to viewer.
**Target Platform**: Existing Ask Lucy web application and API.
**Project Type**: Web application (backend API + frontend).
**Performance Goals**: At least 95% of successful analyses over agreed normal study areas are visible within 10 seconds, excluding clarification; per spec SC-003.
**Constraints**: Respect existing authentication/authorization, provider quotas and request limits; enforce bounded study areas; propagate cancellation; no silent failures; output data coverage/provenance and partial-result status.
**Scale/Scope**: Point and area proximity, within/beyond thresholds, buffers, and multiple non-overlapping distance bands; existing providers and map viewer only.

## Constitution Check

- Clean Architecture: PASS — application contracts and orchestration remain inward-facing; infrastructure adapters perform provider/spatial I/O; API and UI consume public contracts.
- CQRS and API conventions: PASS — analysis is a validated application request exposed by existing versioned spatial endpoint conventions; failures use standard Problem Details.
- Security and data custody: PASS — authorization and scope checks apply to all data and agent/tool paths; limits and provider constraints are retained.
- No silent failures: PASS — provider, validation, partial coverage, and processing failures must be explicitly represented to Lucy/user.
- Frontend/accessibility: PASS — use existing viewer layer interactions and theme/accessibility patterns; preserve 2D/3D behavior.
- Testing/documentation: PASS — add unit, provider integration, endpoint contract, and viewer interaction coverage; keep API documentation synchronized.

## Project Structure

### Documentation (this feature)

```text
specs/093-proximity-analysis/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/proximity-analysis-api.md
└── tasks.md                 # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/AskLucy.Application/       # validated proximity operation and result contracts; agent orchestration integration
src/AskLucy.Infrastructure/    # spatial provider retrieval and distance/buffer processing adapters
src/AskLucy.Web/               # authenticated versioned endpoint and composition
src/AskLucy.Web/ClientApp/src/features/siteAnalysis/  # spatial result layer integration if this is the established analysis surface
src/AskLucy.Web/ClientApp/src/features/viewer/        # viewer layer rendering and controls
src/AskLucy.Web/ClientApp/src/viewer/extensions/       # reuse extension/layer lifecycle

# Corresponding existing project test directories:
 tests/ or src/*Tests/         # locate through repository conventions; add application, infrastructure, API, and UI tests
```

**Structure Decision**: Extend established application/infrastructure/API and viewer feature paths; do not introduce a separate spatial service, datastore, or parallel layer manager. Before implementation, locate the canonical Spatial Request Bundle types and endpoint in the repository, as initial targeted searches did not find a literal bundle type name.

## Complexity Tracking

No constitution violations or new architectural dependencies proposed.
