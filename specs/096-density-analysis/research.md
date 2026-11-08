# Research: Density Analysis

**Date**: 2026-11-08

## D1 — Specialist registration

**Decision**: Add `density.analysis` to `SpatialOperationKinds.Supported` and implement `DensityAnalysisSpecialist : ISpatialSpecialist`.

**Rationale**: The existing registry/coordinator dispatches registered operation kinds and centralizes authorization, user-visible delivery, layers, and failure outcomes.

**Alternatives considered**: A separate chat tool/controller was rejected because it would bypass the spatial coordinator; extending proximity was rejected because distribution is a separate responsibility.

## D2 — Typed Application contracts

**Decision**: Add `DensityAnalysisParameters` on `SpatialOperation`, and define `DensityAnalysisDefinition`, `DensityDatasetEntry`, `IDensityDatasetCatalog`, `IDensityAnalysisProvider`, result/cell/classification records, and audit logger under `Application/Spatial/Density`.

**Rationale**: This follows the existing spatial-query extension seam, protects backward compatibility, keeps Infrastructure dependencies out of Application, and supports deterministic validation.

**Alternatives considered**: Free-form parameters and client-side computation were rejected because they weaken validation, security, reproducibility, and auditing.

## D3 — Explicit compatible methods

**Decision**: Support point-count, point-kernel, and population density only. Catalog metadata declares compatible methods, canonical units, source and provider identities.

**Rationale**: Point methods require point features; population density requires numeric population plus valid area. No incompatible geometry/attribute conversion or population estimate is silently made.

**Alternatives considered**: Treating all geometry as points and estimating population from buildings/POIs were rejected as misleading.

## D4 — Semantic surface layer

**Decision**: Add `density-surface` to backend/client layer unions. Carry cells, values, class breaks, method/unit/effective parameters, coverage, and visualization settings as typed fields.

**Rationale**: Existing `area`/`band` layers do not express density semantics or accessible ramps. A semantic kind retains shared lifecycle without creating another viewer/store.

## D5 — Calculation versus display

**Decision**: Method, radius, cell size, and calculation classification are immutable effective-definition fields. Opacity, palette, visible range, and display classification are per-layer state.

**Rationale**: Restyling cannot alter evidence. Calculation changes become a new named result with a new audit entry.

## D6 — Error handling

**Decision**: Validate malformed layer events and map-render failures into accessible visible feedback with retry/re-run options; provider and timeout outcomes flow through `SpatialSpecialistOutcome`.

**Rationale**: The present map overlay catches errors with `console.error`, and malformed layer events are warn-only; Density must not perpetuate those no-silent-failure violations.

## D7 — Execution bound and audit

**Decision**: Use the existing 30-second linked cancellation deadline and structured audit logging, with complete, partial, empty, failed, and cancelled outcomes.

**Rationale**: It preserves the current in-turn spatial lifecycle and supports auditable limitations without a new job/hub subsystem.
