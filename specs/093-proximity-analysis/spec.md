# Feature Specification: Proximity Analysis

**Feature Branch**: `[093-proximity-analysis]`

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "Extend Lucy's Spatial Analysis capabilities to support proximity analysis of geographic features. Users should be able to ask natural-language questions to identify locations or areas based on their distance from existing POIs or other geographic features. Identify target features, support within/beyond distance queries, retrieve spatial data and calculate proximity, support configurable buffers and multiple ranges, return structured spatial data, visualize in existing 2D/3D map viewer, manage result layers, and integrate with agent orchestration and Spatial Request Bundle."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find Features Near a Target (Priority: P1)
A user asks Lucy to find a feature category within a distance of a target category or geographic feature, such as schools within 1 km of hospitals.

**Why this priority**: This is the primary proximity-analysis journey and delivers immediate spatial insight.

**Independent Test**: Submit a proximity question with known inputs and verify returned features satisfy the requested distance rule and are shown on the map.

**Acceptance Scenarios**:
1. **Given** available school and hospital data, **When** the user asks for schools within 1 km of hospitals, **Then** Lucy returns matching schools and communicates the distance and target used.
2. **Given** the request omits a required category or location, **When** Lucy cannot determine it from conversation context, **Then** Lucy asks a focused follow-up before running the analysis.

### User Story 2 - Identify Underserved Areas (Priority: P1)
A user asks Lucy to identify locations or areas beyond a distance threshold from a feature category, such as residential areas more than 1 km from schools.

**Why this priority**: Beyond-distance analysis supports accessibility and coverage decisions, not just discovery of nearby points.

**Independent Test**: Use a known study area and feature set; verify qualifying areas exclude locations within the threshold and include only areas inside the selected study boundary.

**Acceptance Scenarios**:
1. **Given** residential-area and school data plus a study area, **When** the user asks for residential areas more than 1 km from schools, **Then** the result identifies qualifying areas and reports the threshold.
2. **Given** no study boundary can be established for an area query, **When** analysis is requested, **Then** Lucy asks the user to specify or select the area rather than implying results cover an unlimited region.

### User Story 3 - Compare Distance Bands and Manage Results (Priority: P2)
A user requests multiple distance bands and interacts with the resulting map layers.

**Why this priority**: Distance bands add comparative value; layer controls keep analyses understandable and reversible.

**Independent Test**: Request multiple distance ranges, verify each result is separately identifiable, then hide, remove, and clear result layers.

**Acceptance Scenarios**:
1. **Given** supported features and a study area, **When** the user requests 0–500 m and 500 m–1 km bands, **Then** the response and map distinguish the bands without double-counting boundary locations.
2. **Given** one or more analysis layers are visible, **When** the user hides or removes a layer or clears the analysis, **Then** only the intended layer changes and the map legend/state stays synchronized.

### Edge Cases

- No matching source data, empty results, unsupported feature categories, or unavailable data providers produce clear user-facing explanations and no misleading map layer.
- Ambiguous category, distance, unit, comparison operator, or study area triggers clarification; supported units are converted consistently to meters for analysis and shown in the user's requested unit where possible.
- Invalid, zero, negative, or unreasonably large distances are rejected with an actionable explanation and do not execute analysis.
- Missing or invalid geometries are excluded and counted/reported where material; duplicate source features do not inflate reported counts.
- A feature exactly on a threshold follows a consistent inclusive rule: “within” includes the threshold; “beyond/more than” excludes it. Adjacent distance bands are non-overlapping.
- Provider timeout, partial coverage, rate limits, or spatial-processing failure are surfaced; partial results are labeled as partial and never presented as complete.
- Results crossing the study boundary are clipped for area outputs; point features are selected by their location relative to the study area.
- Switching between 2D and 3D preserves visible analysis layers and their controls.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Lucy MUST interpret a natural-language proximity request into source feature category, target feature/category or geometry, spatial relationship, distance threshold(s), units, and study area when applicable.
- **FR-002**: Lucy MUST ask for missing or materially ambiguous required inputs before executing analysis; it MUST use established conversation or map context when unambiguous.
- **FR-003**: The system MUST retrieve the spatial data required for the requested analysis through the existing Spatial Request Bundle and available spatial data sources, and identify unavailable or incomplete coverage.
- **FR-004**: The system MUST support point-to-point proximity and area-based proximity selection, including within/beyond threshold queries and buffer-based analysis.
- **FR-005**: The system MUST support one or more user-configured distance thresholds and ranges, validate them, use consistent units, and define inclusive/exclusive boundaries as specified in Edge Cases.
- **FR-006**: The system MUST return structured results containing analysis type, resolved inputs, distance thresholds and units, study area, result geometries/features, relevant attributes, result counts, data coverage/limitations, and any warnings.
- **FR-007**: Lucy MUST explain results conversationally, distinguish measured findings from limitations, and avoid claiming completeness when source coverage is partial.
- **FR-008**: The system MUST visualize applicable buffers and selected/highlighted features or areas as distinct, named layers in the existing 2D/3D map viewer.
- **FR-009**: Users MUST be able to show/hide, remove individual analysis layers, and clear all layers for an analysis; layer names, legends, and map contents MUST remain synchronized.
- **FR-010**: Analysis layers MUST remain distinct from existing POI and viewer layers and MUST support the viewer's existing interaction patterns in both 2D and 3D modes.
- **FR-011**: The feature MUST integrate with existing agent orchestration so the agent can request an analysis, receive structured results, and explain them; the agent MUST NOT bypass authorization or spatial request controls.
- **FR-012**: The proximity-analysis request/response MUST be represented in the existing Spatial Request Bundle architecture and exposed through a documented, versioned endpoint contract consistent with existing spatial endpoints. Requests MUST identify operation, features, thresholds, units, and study area; responses MUST follow FR-006. Invalid requests and failures MUST use the platform's standard user-visible error handling.
- **FR-013**: The system MUST constrain analysis to the user's authorized data and requested/confirmed study area, and MUST apply existing request limits and provider constraints.
- **FR-014**: The system MUST handle unsupported categories, no data, empty results, partial data, provider errors, and processing errors without silent failure or misleading results.

### Key Entities

- **Proximity Request**: The interpreted operation, feature categories/geometries, distance thresholds and units, and study area.
- **Analysis Result**: Structured output including qualifying geometries/features, counts, provenance/coverage, and warnings.
- **Analysis Layer**: A user-manageable map representation of a result, such as a buffer, selected features, or qualifying area.
- **Distance Band**: A named non-overlapping range with lower/upper bounds, units, and associated result set.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In acceptance tests with supported categories and unambiguous requests, at least 90% of requests are interpreted with the correct operation, categories, thresholds, units, and study area on the first attempt.
- **SC-002**: For a fixed reference dataset, all returned features and areas match independently calculated expected results, including exact-threshold and adjacent-band cases.
- **SC-003**: At least 95% of successful analyses over the agreed normal test area complete and become visible to the user within 10 seconds, excluding time waiting for user clarification.
- **SC-004**: Users can identify the operation, distance, target, result count, and data-coverage limitation (if any) from the response and map within 30 seconds in usability testing.
- **SC-005**: Users can independently hide, remove, or clear result layers, with map contents and legend synchronized in every tested 2D and 3D workflow.
- **SC-006**: Every tested failure or partial-coverage scenario provides clear user-visible feedback and does not present incomplete results as complete.

## Assumptions

- Existing POI discovery, spatial analysis, Spatial Request Bundle, agent orchestration, and 2D/3D viewer capabilities are reused; this feature adds proximity operations rather than a separate mapping experience.
- Analysis uses available source geometries and reports source coverage and limitations; it does not guarantee that external datasets are exhaustive or current.
- Distance is measured geodesically or using an appropriate projection selected by the spatial analysis capability for the study area, so returned results are meaningful across geographic regions.
- Area-based “beyond” results require a bounded study area; Lucy must obtain one from the request or existing map/conversation context.
- Standard units include meters and kilometers; unsupported or ambiguous units require clarification.
- The API contract is additive within the existing version when feasible; no persistent user-created analysis history is required for the initial release, while layer state lasts for the active viewer session.
- Performance targets apply to agreed normal study areas and data volumes; unusually large requests may be limited or require narrowing the area.
