# Feature Specification: Density Analysis

**Feature Branch**: `[096-density-analysis]`

**Created**: 2026-11-08

**Status**: Draft

**Input**: User description: "Extend Lucy's Spatial Analysis capabilities to support density analysis of geographic datasets."

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Identify Concentration Hotspots (Priority: P1)

A user asks Lucy where a supported geographic feature, such as schools, hospitals, buildings, or reported incidents, is most concentrated in a selected area. Lucy resolves the dataset and study area, selects or confirms an appropriate point-density method and its parameters, presents the concentration pattern on the map, and explains the highest-density locations.

**Why this priority**: Finding concentrations is the core value of density analysis and enables immediate planning and operational insight.

**Independent Test**: Ask for building density in a known study area with a known concentration; verify that the hotspot is classified higher than surrounding areas, visible as a controllable map layer, and explained conversationally.

**Acceptance Scenarios**:

1. **Given** a supported point-feature dataset and a confirmed study area, **When** the user asks where features are most concentrated, **Then** Lucy returns a density surface, identifies the highest-density areas, and states the dataset, method, parameters, units, and coverage used.
2. **Given** the request has an unambiguous dataset and map context but no radius or cell size, **When** the user asks for density analysis, **Then** Lucy uses documented context-appropriate defaults and tells the user that defaults were used.
3. **Given** the request does not identify a resolvable dataset or bounded study area, **When** density analysis is requested, **Then** Lucy asks a focused clarification and does not run an unbounded or guessed analysis.

---

### User Story 2 - Find Low-Coverage Areas (Priority: P1)

A user asks Lucy to identify areas with low service or population coverage, such as neighbourhoods with few hospitals or areas of low population density. Lucy returns the lowest classified density areas within the study area and clearly distinguishes observed low density from a claim that a service need is proven.

**Why this priority**: Low-density patterns are essential for identifying possible coverage gaps and are the counterpart to hotspot discovery.

**Independent Test**: Run a low-coverage request over a known grid containing sparse and dense locations; verify that low-density cells are correctly classified and that a result layer and conversational caveat are provided.

**Acceptance Scenarios**:

1. **Given** point features and a confirmed boundary, **When** the user asks where hospital coverage is lowest, **Then** Lucy identifies the lowest-density areas inside that boundary and communicates that this is a spatial concentration finding, not proof of unmet demand.
2. **Given** an available population dataset with values and associated areas, **When** the user asks for population density, **Then** Lucy calculates and displays people per unit area using the source dataset's supported geography.
3. **Given** a point dataset cannot support a requested population-density question, **When** the user submits the request, **Then** Lucy explains the data limitation and offers a supported feature-density analysis where appropriate.

---

### User Story 3 - Adjust a Density Layer (Priority: P2)

A user refines a completed density analysis by adjusting radius, cell size, classification, opacity, or colour range, then independently manages the resulting layer alongside other spatial analysis layers.

**Why this priority**: Density results are sensitive to analysis and display parameters; transparent adjustment enables users to interpret them responsibly.

**Independent Test**: Create two density layers from the same dataset using materially different parameters, verify their legends and labels disclose the differences, then hide and remove one without affecting the other.

**Acceptance Scenarios**:

1. **Given** an existing density layer, **When** the user changes an allowed analysis parameter, **Then** the system creates or updates a clearly labelled density result whose legend records the effective parameter values.
2. **Given** one or more density layers are visible, **When** the user changes opacity, colour scheme, visible value range, or classification display, **Then** the visual change takes effect without altering the underlying calculated values.
3. **Given** multiple analysis layers are present, **When** the user hides, removes, or clears a density layer, **Then** only the requested layer changes and the map legend and layer controls remain synchronized in 2D and 3D views.

---

### User Story 4 - Receive Trustworthy Results and Failures (Priority: P2)

A user receives an understandable result when source coverage is incomplete, no usable data is found, parameters are invalid, or the calculation cannot complete.

**Why this priority**: Density visualizations can be persuasive; transparent limits and explicit failures are required for informed decisions.

**Independent Test**: Exercise unsupported datasets, empty inputs, partial coverage, invalid parameters, cancellation, and processing failure; verify that each has an explicit user-visible outcome and never leaves an unlabeled or misleading layer.

**Acceptance Scenarios**:

1. **Given** source data is incomplete or unavailable for part of the study area, **When** analysis completes or cannot complete, **Then** Lucy visibly states the limitation and does not present the result as complete.
2. **Given** a request has an invalid radius, cell size, classification, or incompatible method, **When** validation occurs, **Then** Lucy gives an actionable explanation and no calculation is run.

### Edge Cases

- An unbounded study area is never inferred for grid, hotspot, or low-coverage analysis; Lucy requests a boundary, selected area, or unambiguous map extent.
- A target dataset is unsupported, unavailable, empty, contains unusable geometry, or has insufficient spatial coverage: Lucy explains which condition applies, reports material exclusions where possible, and creates no misleading result layer.
- Zero, negative, non-finite, incompatible, or excessively large radius/cell-size values are rejected with limits or a narrowing suggestion; they are never silently changed.
- For point density, duplicate source identities are counted once; a clear documented rule is used for features on a study boundary and cells intersecting it.
- Population density is only calculated from an available population value and a valid associated area; zero-area, missing-value, or incompatible-area records are excluded and disclosed when material.
- A requested density method cannot be used for the resolved geometry or attributes: Lucy offers a compatible supported method or asks for a different dataset, rather than substituting a method silently.
- Empty calculated surfaces, a flat distribution with no meaningful class separation, partial coverage, timeout, cancellation, provider failure, and rendering failure all result in an explicit user-visible status. Partial results are labelled partial and never represented as complete.
- A user changes the active map region, removes a layer, or clears the analysis while it is running: stale results are not added silently; the request is explicitly cancelled or delivered as a newly named result consistent with the orchestrator lifecycle.
- Switching between 2D and 3D preserves density-layer visibility, legend, effective parameters, and interactive inspection.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Lucy MUST interpret a natural-language density request into the target dataset, requested finding (high density, low density, coverage gap, or population density), a bounded study area, and any user-specified analysis or visualization parameters.
- **FR-002**: Lucy MUST use established conversation and map context when unambiguous, and MUST ask a focused clarification before execution when the target dataset, study area, intended density meaning, required population measure, or a materially ambiguous parameter cannot be resolved.
- **FR-003**: The feature MUST support the following density methods: point-count density, point-kernel density, and population density calculated from an available population value divided by its associated geographic area. The method used MUST be stated in the result.
- **FR-004**: Point-count density MUST report feature count per area; point-kernel density MUST report a smoothed feature-density estimate per area; population density MUST report people per area. Results and legends MUST state the applicable unit.
- **FR-005**: The system MUST only execute a method compatible with the resolved dataset: point-count and point-kernel methods require supported point features; population density requires valid population values and associated areas. It MUST not convert incompatible source geometry or attributes into a density result without explicitly supported rules.
- **FR-006**: The system MUST support configurable search radius and cell size for applicable point-density methods, and configurable density classification for calculated results. It MUST validate values against documented operational limits and disclose all effective values, including defaults.
- **FR-007**: The system MUST support density classification displays using equal interval, quantile, and natural-break classes, plus a continuous scale where classification is not requested. Classification changes MUST not modify calculated density values.
- **FR-008**: The system MUST identify and return high-density hotspots and low-density areas according to the selected or stated classification, while clearly distinguishing a concentration pattern from a causal, service-quality, or demand conclusion.
- **FR-009**: Each execution MUST produce a structured Density Analysis Result containing the resolved request, dataset and source coverage, compatible method, effective parameters, units, geographic extent, calculated cells or areas with values and classifications, hotspot and low-density summaries, warnings, status, and provenance sufficient to explain the result.
- **FR-010**: Density Analysis MUST register as a named specialist under the existing Spatial Analysis Orchestrator and use the versioned Spatial Request Bundle architecture. It MUST not introduce a parallel interpretation, authorization, dispatch, or delivery path.
- **FR-011**: The Density Analysis request contract MUST include an operation type of `density`, target dataset, study area, method or automatic method selection, optional radius, optional cell size, optional classification, and optional visualization preferences. The request is invalid when it lacks required compatible inputs or carries an unsupported contract version, and it MUST receive a clear user-visible failure without execution.
- **FR-012**: The Density Analysis response contract MUST provide a version, request identifier, status, resolved inputs, effective parameters and units, result extent, structured density values/geometries, hotspot and low-density summaries, source coverage/provenance, warnings, and one or more renderable layer descriptors. Error, empty, partial, and cancelled outcomes MUST be distinguishable from a successful complete result.
- **FR-013**: The spatial analysis backend MUST execute validated density calculations inside the confirmed study area and under the requesting user's existing authorization, rate, and data-access constraints.
- **FR-014**: The existing 2D/3D map viewer MUST render each density result as an independently named, interactive heatmap or graduated-colour surface with an accessible legend showing method, unit, effective radius and cell size when applicable, classification, colour meaning, and data-coverage warnings.
- **FR-015**: Users MUST be able to adjust permitted visualization parameters — opacity, colour scheme, visible value range, and classification display — independently for each density layer. These adjustments MUST not recalculate or mutate the source density values unless the user changes an analysis parameter.
- **FR-016**: Users MUST be able to show, hide, remove, and clear density layers independently of other analysis and viewer layers. Layer names, legends, map contents, and 2D/3D state MUST remain synchronized.
- **FR-017**: Lucy MUST explain findings conversationally, naming the dataset, geographic scope, method, effective parameters, result units, highest and lowest areas, source limitations, and whether results are complete, partial, empty, cancelled, or failed.
- **FR-018**: Every executed density operation MUST produce an immutable audit entry containing authenticated user identity, request and result identifiers, resolved dataset and study area, requested and effective method/parameters, outcome, duration, source coverage state, result summary, and the identities or aggregate provenance of source records used. End users MUST not be able to delete audit entries.
- **FR-019**: The system MUST provide a visible, actionable outcome for validation failures, unavailable data, insufficient coverage, empty results, timeouts, cancellation, calculation errors, and rendering errors. No density request or failure may end silently.

### Key Entities

- **Density Analysis Bundle**: The versioned density operation carried by the Spatial Request Bundle; records the target dataset, study area, requested finding, method, analysis parameters, and display preferences.
- **Density Method**: A supported way to calculate density: point-count, point-kernel, or population density. Defines compatible input data, output measure, and unit.
- **Density Analysis Result**: Structured outcome of an analysis, including resolved inputs, effective parameters, calculated geometries and values, classifications, summaries, coverage, provenance, warnings, and outcome status.
- **Density Cell or Area**: A bounded geographic result element holding a density value, unit, classification, and geometry that can be rendered and inspected.
- **Density Layer**: A user-managed map representation of one Density Analysis Result, with analysis metadata, visualization settings, legend, visibility state, and renderable surface.
- **Density Classification**: The rule and thresholds that group or display values as low through high without changing the underlying values.
- **Density Audit Entry**: Immutable record of an executed density operation, sufficient to reconstruct who requested it, what was calculated, with which effective parameters and source coverage, and its outcome.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In acceptance tests using supported, unambiguous requests, at least 90% of density requests are interpreted with the correct dataset, study area, density intent, method, and explicitly supplied parameters on the first attempt.
- **SC-002**: For fixed reference datasets, calculated density values, classifications, hotspots, and low-density areas match independently verified expected results in 100% of representative test cases, including boundary, duplicate, flat-distribution, and missing-record cases.
- **SC-003**: At least 95% of successful analyses for an agreed normal study area become visible with a complete legend and initial conversational summary within 10 seconds, excluding time spent waiting for clarification.
- **SC-004**: In usability testing, at least 90% of users can identify the dataset, method, unit, effective parameters, highest-density area, and any material data limitation from the result and map within 30 seconds.
- **SC-005**: Users can independently adjust a density layer's display settings and show, hide, remove, or clear it without changing another layer in 100% of tested 2D and 3D flows.
- **SC-006**: Every tested validation failure, data limitation, empty result, partial result, cancellation, calculation failure, and rendering failure produces a clear user-visible status, with no misleading layer or silent outcome.
- **SC-007**: Every executed density operation has an immutable, operator-verifiable audit entry containing the required identity, resolved inputs, effective parameters, coverage state, result summary, and outcome.

## Assumptions

- The existing Spatial Analysis Orchestrator, Spatial Request Bundle, authenticated request path, and 2D/3D viewer layer system specified in spec 094 are available and reused.
- Density layers are session-scoped like other spatial analysis layers; this feature does not introduce persistent saved analyses or a new map viewer.
- The initial supported point-feature datasets include available geographic features such as buildings, schools, hospitals, and other catalogued point datasets. A dataset is only eligible when its metadata declares a compatible density method.
- Population density is available only where a catalogued population dataset provides a numeric population value and valid associated geographic area; the feature does not estimate population from unrelated features.
- If no point-density radius or cell size is specified, Lucy may select documented defaults appropriate to the confirmed study area's scale and must disclose them. A missing study area is never defaulted to the whole world.
- Default classification is a continuous scale unless a discrete classification is requested or necessary for a hotspot/low-density answer; all classification choices are disclosed.
- Hotspots and low-density areas describe relative concentration patterns in the analysed data. They do not establish need, accessibility, causation, compliance, or completeness unless separately supported by additional analysis.
- Performance targets apply to agreed normal study areas and source volumes. Requests exceeding published limits require the user to narrow the area or parameters.
- Existing authorization, rate limiting, auditing, error-handling, and source-coverage rules apply without exception.
