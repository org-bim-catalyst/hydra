# Feature Specification: Overlay Analysis

**Feature Branch**: `[097-overlay-analysis]`

**Created**: 2026-11-08

**Status**: Draft

**Input**: User description: "Extend Lucy's Spatial Analysis capabilities to support overlay analysis by combining multiple geographic datasets to identify areas satisfying multiple spatial criteria."

## Overview

Overlay analysis lets a user ask Lucy a multi-criteria location question in ordinary language, such as finding available parcels with high population, good road access, and sufficient separation from existing facilities. Lucy resolves the requested datasets and criteria, validates a bounded study area, and sends one or more overlay operations through the existing Spatial Analysis Agent and versioned Spatial Request Bundle. The spatial backend returns structured geometries, scores where requested, coverage and provenance; the existing viewer displays the source and result layers independently in 2D and 3D. Lucy explains which criteria were applied and how candidate locations were ranked.

This feature adds no parallel orchestration path, standalone spatial endpoint, data provider, or persistent layer store. It builds on the spatial orchestrator (spec 094), spatial query/data catalog (spec 095), density analysis (spec 096), and existing viewer layer lifecycle.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find locations meeting several conditions (Priority: P1)

A planner describes a location-selection task, for example: "Find vacant parcels with high population within 500 metres of a major road and at least 2 km from an existing clinic." Lucy identifies the parcel, population, road, and facility datasets, converts the request to explicit predicates and overlay operations, and returns only candidate areas satisfying all required conditions. It names any assumptions and reports the source coverage.

**Why this priority**: Multi-condition site selection is the central user value of overlay analysis.

**Independent Test**: Run a natural-language request against fixed parcel, population, road, and clinic fixtures. Verify the resolved datasets and criteria and compare returned geometries with independently known candidates.

**Acceptance Scenarios**:

1. **Given** supported datasets and a bounded study area are available, **When** the user requests locations satisfying multiple criteria, **Then** Lucy resolves the datasets and operations and returns only geometries satisfying every required condition.
2. **Given** the request's dataset, area, or criterion is ambiguous, **When** Lucy interprets it, **Then** Lucy asks a focused question and performs no overlay until the ambiguity is resolved.
3. **Given** a condition refers to an unavailable or unsupported dataset, **When** the request is evaluated, **Then** Lucy identifies the unavailable condition and does not present results as satisfying the complete request.

### User Story 2 - Combine and compare spatial layers (Priority: P1)

A user asks Lucy to intersect, union, or subtract geographic layers, or asks which features intersect another layer. Lucy uses the operation that matches the described intent, returns valid geometries and counts, and explains what each operation means in the result.

**Why this priority**: Explicit overlay operations are the foundation for combining geographic evidence and are independently useful without scoring.

**Independent Test**: Apply each supported operation to small fixed polygon and feature fixtures; compare result geometry, counts, and provenance to expected outputs.

**Acceptance Scenarios**:

1. **Given** two compatible polygon layers, **When** the user asks for their common area, **Then** Lucy performs geometric intersection and returns the overlapping area.
2. **Given** compatible layers, **When** the user requests their combined coverage or the part of one layer outside another, **Then** Lucy performs union or difference respectively and labels the output accordingly.
3. **Given** the user asks which features from one dataset touch or fall within another, **When** the request is run, **Then** Lucy returns the matching features as a spatial relationship selection without implying that their geometries were geometrically combined.

### User Story 3 - Rank candidates using weighted criteria (Priority: P2)

A user asks Lucy to rank suitable locations based on factors such as population, road access, land availability, or distance to facilities. Lucy applies stated weights or asks for clarification when weights materially affect the outcome; the result includes criterion-level scores, the combined score, and the scoring direction so the ranking can be understood.

**Why this priority**: Ranking helps users compare eligible sites after hard constraints have narrowed the candidate set.

**Independent Test**: Run a fixed candidate set with declared weights and known normalized input values; verify per-criterion and combined scores and ordering.

**Acceptance Scenarios**:

1. **Given** the user supplies valid criterion weights, **When** candidates are scored, **Then** each candidate includes criterion scores, the effective weights, the aggregate score, and its rank.
2. **Given** the user requests ranking but provides no weights, **When** the criteria can be scored using documented equal weights and compatible directions, **Then** Lucy uses equal weights and clearly discloses this default; otherwise it asks how to weight the criteria.
3. **Given** a candidate fails a required hard constraint, **When** suitability is calculated, **Then** the candidate is excluded and is not reintroduced by a high weighted score.

### User Story 4 - Inspect and manage overlay layers (Priority: P2)

A user reviews source layers and overlay results on the existing map, toggles each layer, inspects result attributes and scoring, and removes one or all analysis layers without affecting other viewer layers. The same layer state remains usable after switching between 2D and 3D.

**Why this priority**: Independent layer controls and inspection are necessary to understand and trust multi-source results.

**Independent Test**: Produce an analysis with multiple inputs and outputs, toggle and remove each layer, inspect a candidate, then switch map modes and verify state and legends remain synchronized.

**Acceptance Scenarios**:

1. **Given** an overlay produces several source and result layers, **When** the user hides or removes one, **Then** only that layer changes and the legend remains synchronized.
2. **Given** a candidate feature is selected, **When** its details are inspected, **Then** Lucy shows the relevant input attributes, applied conditions, score breakdown, and source provenance available for that result.
3. **Given** the viewer changes between 2D and 3D, **When** the display updates, **Then** layer visibility, names, legends, and inspectable results are preserved.

### User Story 5 - Understand limits and failures (Priority: P2)

A user receives a clear outcome when datasets have mismatched coordinate references, invalid geometries, incomplete coverage, incompatible geometry types, no matching candidates, or an execution error. Successful parts of a multi-operation request remain available when independent operations fail, and Lucy identifies what is incomplete.

**Why this priority**: Overlay outcomes can look authoritative; users need to know when data or processing limits affect the conclusion.

**Independent Test**: Exercise empty, partial, incompatible, invalid, timed-out, and partially failed inputs; confirm that result status and user-visible explanation match each case.

**Acceptance Scenarios**:

1. **Given** a required input dataset is only partially available, **When** an overlay completes, **Then** the result is marked partial and identifies the missing coverage.
2. **Given** an independent operation fails while another succeeds, **When** the bundle completes, **Then** the successful result is delivered and the failed operation is named in the explanation.
3. **Given** the operation succeeds but yields no matches, **When** Lucy responds, **Then** the result is identified as a successful empty result, distinct from an execution failure.

### Edge Cases

- No bounded study area can be resolved: Lucy asks for an area and never runs an unbounded overlay.
- A requested dataset name maps to multiple catalogs or incompatible layers: Lucy asks the user to select the intended dataset.
- A required source is unavailable or lacks coverage in part of the study area: report partial or failed status and never describe the candidate set as complete.
- Inputs use the existing WGS84 latitude/longitude spatial contract. Sources that cannot supply WGS84 coordinates are unsupported and must fail visibly; adding arbitrary source CRS metadata/transforms is outside this feature because the current dataset/provider contract has no CRS declaration.
- Invalid, empty, self-intersecting, or otherwise unusable geometries are rejected, repaired only under an explicit documented rule, or excluded with a visible warning and counts.
- Geometry types are incompatible with the requested operation: explain the incompatibility and request compatible inputs; do not silently substitute a different operation.
- Union or intersection creates multipart, hole-containing, or geometry-collection results: preserve valid geometry or report unsupported output explicitly.
- Difference direction is ambiguous: ask which layer is the minuend and which is subtracted.
- Distance-to-road/facility thresholds omit a distance or unit: infer a unit only when dataset metadata or established context makes it unambiguous; otherwise ask.
- A weight is negative, non-finite, incompatible with the score direction, or all weights sum to zero: reject with a clear correction request.
- The same feature occurs in multiple layers: preserve source identity and lineage; do not merge distinct records just because their geometries overlap.
- A result exceeds rendering limits: return an explicit truncation/generalization status and visible counts; never silently discard features.
- A layer is removed or cleared while processing is active: honor orchestrator cancellation/lifecycle rules and provide one visible completion or cancellation outcome.
- A 2D/3D renderer cannot display an output geometry: retain the structured result where possible and show a visible rendering error with a usable explanation.

## Requirements *(mandatory)*

### Functional Requirements

**Interpretation and criteria**

- **FR-001**: Lucy MUST resolve user-facing dataset descriptions to available catalog datasets and identify the role of each dataset in the request, such as candidate features, constraint areas, roads, population, or existing facilities.
- **FR-002**: Lucy MUST convert natural-language criteria into explicit, typed conditions, including attribute thresholds, area availability, spatial relationships, and distance thresholds when supported by the catalog.
- **FR-003**: Lucy MUST resolve a bounded study area from explicit user input or validated current map context; if no suitable area exists, it MUST ask before executing.
- **FR-004**: Lucy MUST distinguish required eligibility conditions from optional ranking criteria. All required conditions MUST be satisfied for a candidate to qualify.
- **FR-005**: Lucy MUST ask a focused clarification when dataset mapping, study area, units, difference direction, criterion meaning, or required scoring behavior is ambiguous enough to change results materially.

**Supported operations and execution**

- **FR-006**: Overlay Analysis MUST support geometric intersection, geometric union, and directional difference for compatible input geometries.
- **FR-007**: Overlay Analysis MUST support spatial intersection selection, which selects features based on intersecting/within relationships without claiming that their geometry was combined.
- **FR-008**: A request MAY contain a dependency-ordered sequence or graph of overlay operations. Independent operations MUST remain eligible for the existing coordinator's concurrent execution; dependent operations MUST wait for their inputs.
- **FR-009**: The backend MUST execute only validated operations within the confirmed study area and under existing user authorization, rate, and data-source constraints.
- **FR-010**: The backend MUST preserve source identity and operation lineage for result features and report counts of included, excluded, invalid, and unavailable input records when those counts can be determined.

**Scoring**

- **FR-011**: The system MUST support optional multi-criteria scoring for eligible candidates using normalized criterion values, explicit criterion direction, effective weights, per-criterion scores, aggregate score, and deterministic rank.
- **FR-012**: User-supplied weights MUST be validated, recorded, and disclosed. If weights are omitted, the system MAY use equal weights only when criterion direction and normalization are well-defined, and MUST disclose that default; otherwise it MUST ask.
- **FR-013**: A weighted score MUST NOT override a failed required eligibility condition. Lucy MUST distinguish eligibility conditions from preferences used only for ranking.
- **FR-014**: Lucy MUST explain the criteria and effective weights or defaults behind any ranking and MUST avoid presenting a score as an objective probability or proof of suitability.

**Spatial Request Bundle and endpoint contracts**

- **FR-015**: Overlay Analysis MUST register as a named specialist under the existing Spatial Analysis Agent and use the current versioned Spatial Request Bundle. It MUST NOT create a separate orchestration or user-facing execution endpoint.
- **FR-016**: The overlay operation request contract MUST identify `operationType: overlay`, a stable operation identifier, one or more input dataset/layer references and their roles, a bounded study area or resolvable area reference, the requested operation or operation steps, typed criteria, and optional scoring and display preferences.
- **FR-017**: Each operation step MUST identify its operation kind (`intersection`, `union`, `difference`, or `spatialIntersection`), its input references, and any operation-specific relationship or direction. A bundle MUST identify dependencies where an operation consumes another step's output.
- **FR-018**: The result contract MUST include contract version, request and operation identifiers, status (`succeeded`, `empty`, `partial`, `failed`, or `cancelled`), resolved inputs and criteria, output geometries and relevant attributes, counts, coverage, warnings, provenance/lineage, optional scoring details and ranks, and renderable layer descriptors.
- **FR-019**: The existing authenticated chat request and spatial result delivery contracts MUST carry the operation and normalized result. The result MUST be delivered using the existing spatial layer event/result mechanism; layer visibility and removal MUST continue to use the existing client-owned layer state. No new persistent layer endpoint is required.
- **FR-020**: Invalid bundle versions, missing inputs, incompatible geometry/operation pairs, invalid weights, and unresolved study areas MUST produce a clear failure or clarification before execution and MUST NOT be interpreted as successful empty results.

**Visualization and lifecycle**

- **FR-021**: The existing 2D/3D viewer MUST render source and output geometries as independently named layers, visually distinguish inputs from overlay outputs, and highlight qualifying candidate areas.
- **FR-022**: Each layer MUST expose independent visibility and removal controls; clearing an overlay analysis MUST remove only layers belonging to that analysis and MUST NOT change conversation history or unrelated viewer layers.
- **FR-023**: The viewer MUST provide an accessible legend identifying layer role, operation, key criteria, score meaning where applicable, and material source-coverage or processing warnings.
- **FR-024**: Users MUST be able to inspect result geometries and view relevant attributes, applied conditions, scores/rank, and available provenance without issuing a second spatial query.
- **FR-025**: Layer names, map rendering, legend, visibility, and inspection state MUST remain synchronized across 2D/3D mode changes and independent layer toggles/removals.

**Transparency and auditability**

- **FR-026**: Lucy MUST provide a user-visible summary naming the study area, datasets, operations, applied hard constraints, scoring approach if used, result count, and any partial coverage, exclusions, or warnings.
- **FR-027**: Every operation MUST end with a visible result, clarification, cancellation, or failure. Exceptions and failed operations MUST be surfaced through the established error path; they MUST NOT be swallowed or represented as empty success.
- **FR-028**: Each executed overlay operation MUST produce an immutable audit record containing authenticated user identity, request/operation IDs, resolved dataset references and study area, operation steps and criteria, effective weights if any, outcome, duration, coverage, result counts, and source provenance available under the existing audit policy.

### Endpoint Contracts

Overlay is a spatial operation carried through existing chat orchestration, not a new standalone route.

**Request (inside current Spatial Request Bundle)**

```json
{
  "operationId": "overlay-1",
  "operationType": "overlay",
  "studyArea": { "source": "currentMapBoundary" },
  "inputs": [
    { "ref": "parcels", "datasetId": "land-parcels", "role": "candidate" },
    { "ref": "population", "datasetId": "population-areas", "role": "criterion" },
    { "ref": "roads", "datasetId": "major-roads", "role": "constraint" },
    { "ref": "clinics", "datasetId": "health-facilities", "role": "constraint" }
  ],
  "steps": [
    { "stepId": "eligible", "kind": "spatialIntersection", "inputs": ["parcels", "population"] },
    { "stepId": "outside-clinics", "kind": "difference", "inputs": ["eligible", "clinics"] }
  ],
  "criteria": [
    { "criterionId": "available", "kind": "attribute", "inputRef": "parcels", "field": "availability", "operator": "equals", "value": "available", "required": true },
    { "criterionId": "road-access", "kind": "distance", "inputRef": "roads", "operator": "within", "value": 500, "unit": "m", "required": true },
    { "criterionId": "clinic-separation", "kind": "distance", "inputRef": "clinics", "operator": "atLeast", "value": 2000, "unit": "m", "required": true }
  ],
  "scoring": null
}
```

Fields illustrate the semantic contract and are not a demand to introduce a new HTTP route or duplicate catalog definitions. The planner/backend MUST validate dataset IDs, fields, geometry compatibility, units, references, and dependency order against available catalogs.

**Normalized result (through existing spatial result/layer delivery)**

```json
{
  "version": 1,
  "requestId": "request-id",
  "operationId": "overlay-1",
  "status": "succeeded",
  "resolvedStudyArea": { "type": "Polygon", "coordinates": [] },
  "inputs": [{ "ref": "parcels", "datasetId": "land-parcels", "coverage": "complete" }],
  "steps": [{ "stepId": "eligible", "kind": "spatialIntersection", "resultCount": 12 }],
  "criteria": [{ "criterionId": "road-access", "effectiveValue": 500, "unit": "m", "required": true }],
  "features": [{ "featureId": "candidate-1", "geometry": {}, "attributes": {}, "criteria": {}, "score": null, "rank": null, "provenance": [] }],
  "counts": { "matched": 12, "excluded": 3, "invalid": 0 },
  "scoring": null,
  "warnings": [],
  "layers": [{ "layerId": "overlay-request-id", "name": "Suitable parcels", "role": "result", "geometryType": "Polygon" }]
}
```

The implementation MUST use the established Spatial Request Bundle and normalized result types, extending them compatibly for overlay semantics. Exact serialization names can follow current repository conventions while preserving the required information and status distinctions above.

### Key Entities

- **Overlay Operation**: One requested composition of geographic inputs; identifies operation steps, inputs, study area, constraints, optional scoring, and dependencies.
- **Overlay Input**: A catalog-backed dataset or layer reference with its role, coverage, geometry type, coordinate reference, and source identity.
- **Overlay Criterion**: A typed eligibility condition or ranking preference with operator, value, unit, required/optional status, and scoring direction where relevant.
- **Overlay Result Feature**: A returned geometry with stable identity, relevant source attributes, criteria outcomes, provenance, and optional score/rank.
- **Overlay Result**: The structured operation outcome, including resolved inputs, step results, counts, coverage, warnings, and normalized result features.
- **Overlay Layer**: A client-managed map representation of an input or result, independently named, styled, inspected, toggled, and removed.
- **Scoring Profile**: Criterion normalization/direction and effective weights used to produce comparable candidate scores and ranks.
- **Overlay Audit Entry**: An immutable record of the request interpretation, execution, source coverage, result summary, and outcome.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In acceptance cases with supported data and unambiguous requests, at least 90% of users' natural-language criteria are resolved to the intended datasets, operations, thresholds, and bounded area without a correction turn.
- **SC-002**: On fixed reference datasets, intersection, union, directional difference, and spatial intersection outputs match independently verified expected geometries and feature counts in 100% of representative cases.
- **SC-003**: Every returned candidate satisfies all required eligibility conditions in 100% of fixed-fixture acceptance cases; candidates failing a hard condition never appear as eligible regardless of score.
- **SC-004**: When scoring is requested, all returned scores and ranks are reproducible from the disclosed criterion values, normalization, direction, and weights in 100% of fixed-fixture cases.
- **SC-005**: At least 95% of successful overlays for an agreed normal study area display the first useful result layer and a status summary within 10 seconds, excluding clarification time.
- **SC-006**: In usability review, at least 90% of users can identify the source datasets, applied conditions, result completeness, and score meaning (when present) within 30 seconds.
- **SC-007**: In all tested 2D/3D flows, users can independently toggle and remove every overlay layer while the legend and remaining layers stay synchronized.
- **SC-008**: 100% of validation failures, empty results, partial coverage, cancellations, backend failures, and rendering failures produce a visible status that accurately distinguishes the outcome.
- **SC-009**: Every executed overlay operation has a queryable immutable audit record containing the user, resolved request, inputs, criteria, outcome, duration, coverage, and result counts.

## Assumptions

- The Spatial Analysis Agent, versioned Spatial Request Bundle, normalized result delivery, and client-owned map layer controls from specs 094–096 are available and remain the single execution and visualization path.
- Existing spatial backend catalogs/providers supply the datasets; initial overlay support is limited to datasets and geometry/attribute operations they declare compatible. This feature does not promise a new dataset source.
- A study area is required for bounded execution and is resolved from explicit request or validated map context; no global/unbounded analysis is allowed.
- Initial distance-based criteria use dataset-declared units and supported distance calculations. Lucy asks when an input's meaning or unit is materially ambiguous.
- Weighted ranking is optional. Eligibility constraints are evaluated before scoring. Equal weights are used only where normalization and score direction are defined and Lucy discloses them.
- Overlay and input layers are session-scoped and use the viewer's existing lifecycle; this feature introduces no durable result storage or new layer-management endpoint.
- Existing authentication, authorization, rate limiting, audit retention, cancellation, and error-handling policies apply.
- The viewer's existing polygon, line, point, marker, buffer, and area-highlight representations are reused. New unsupported geometry visualization is surfaced as a rendering limitation rather than silently omitted.
