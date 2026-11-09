# Research: Overlay Analysis

**Date**: 2026-11-08

## D1 — Specialist and request lifecycle

**Decision**: Add `overlay.analysis` to the supported spatial-operation catalog and implement `OverlayAnalysisSpecialist : ISpatialSpecialist`. Carry user input and results through the existing spatial bundle, coordinator, chat event, and client-owned layer lifecycle.

**Rationale**: The coordinator already owns location resolution, dependency ordering, parallel dispatch, normalization, cancellation, and user-visible result delivery. A separate endpoint or chat capability would duplicate these rules.

**Alternatives considered**: A bespoke API/controller and client-side operation execution were rejected because they bypass shared validation, authorization, audit, and failure behavior.

## D2 — Catalog-backed inputs and geometry execution

**Decision**: Overlay definitions reference registered dataset IDs and bounded study areas. Infrastructure retrieves geometry and attributes through existing spatial query/provider patterns and performs supported geometry operations with the existing NetTopologySuite dependency.

**Rationale**: The project already depends on NetTopologySuite in Infrastructure and returns typed Point/Polygon/LineString feature geometries for spatial queries. Reusing those seams avoids a new library or a duplicate data-access implementation.

**Alternatives considered**: Application-layer geometry calculations were rejected to preserve the dependency rule. Adding another geometry dependency or relying on model-generated coordinates was rejected as unnecessary and unsafe.

## D3 — Operation and predicate model

**Decision**: Model geometric `intersection`, `union`, and directional `difference` separately from `spatialIntersection`, which selects source features by a declared relationship without replacing their geometry. Bundle steps reference named inputs and prior step results; dependencies are validated before dispatch.

**Rationale**: Users need reliable differences between geometry construction and feature selection. The existing coordinator already supports dependency-ordered operations.

**Alternatives considered**: Treating every spatial match as geometric intersection was rejected because it misstates output geometry. Implicit difference direction was rejected because subtraction is non-commutative.

## D4 — Eligibility and scoring

**Decision**: Apply required eligibility criteria first. Apply optional ranking after eligibility with per-criterion normalized values, direction, validated weights, aggregate score, and stable tie-breaking. Use equal weights only when every criterion is comparable and direction is known; disclose it.

**Rationale**: A score must not override a failed hard condition and must be explainable and reproducible from the returned result.

**Alternatives considered**: Opaque model-generated ranks or scoring before hard filters were rejected because rankings would be untestable and could return ineligible sites.

## D5 — Result visualization and lifecycle

**Decision**: Deliver polygon, line, and point output as the existing typed spatial feature geometry through the current layer event. Extend result metadata with operation/criteria/score/provenance where needed; keep visibility and removal client-owned.

**Rationale**: The viewer already renders feature-query polygons and lines and provides map feature inspection. This retains the existing 2D/3D layer lifecycle.

**Alternatives considered**: A new overlay-only viewer, persistent result endpoint, or separate layer manager was rejected as duplicate complexity.

## D6 — Coverage and failures

**Decision**: Preserve per-input source coverage and report complete, partial, empty, failed, or cancelled results distinctly. Invalid/untransformable geometries and unavailable sources are surfaced in the summary and warnings; no silent repair or substitution is allowed.

**Rationale**: Overlay omissions can materially change candidate suitability, so incomplete coverage must be visible in both structured outcomes and user-facing narration.

**Alternatives considered**: Returning only valid feature count or silently repairing coordinates was rejected because it conceals data loss and can mislead planning decisions.

## D7 — Coordinate reference scope

**Decision**: Reuse the existing WGS84 `GeoPoint` latitude/longitude contract for v1. Do not add source CRS metadata or a transform registry as part of Overlay Analysis.

**Rationale**: The existing dataset and spatial query provider contracts do not declare coordinate reference systems. Pretending to compare unknown CRSs would be unsafe; a new CRS subsystem would exceed the bounded feature request. Providers must supply WGS84 values or fail before contributing features.
