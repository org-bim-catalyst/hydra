# Feature Specification: Spatial Query Analysis

**Feature Branch**: `095-spatial-query-analysis`

**Created**: 2026-11-06

**Status**: Draft

**Input**: User description: "Extend Lucy's Spatial Analysis capabilities to support dynamic spatial and attribute-based queries against geographic datasets. Users should be able to ask Lucy natural-language questions to identify geographic features that satisfy specific conditions. Allow Lucy to identify the target dataset and geographic features, such as land parcels, buildings, roads, and development areas. Support attribute-based conditions, such as parcel area greater than 5,000 m² or buildings constructed within a specified year. Support spatial conditions, such as within, outside, intersecting, containing, or near another geographic feature. Support combining multiple attribute and spatial conditions in a single query. Allow Lucy to identify missing information and ask users for clarification when necessary. Execute queries through the spatial analysis backend and return matching features as structured spatial data. Visualize results in the existing 2D/3D map viewer using highlighted features and dedicated map layers. Display relevant feature attributes when users select a result. Allow users to manage, clear, and remove analysis result layers independently. Integrate with Lucy's existing agent orchestration and Spatial Request Bundle architecture."

---

## Overview

Lucy can already identify points of interest, measure proximity, and visualize spatial results through the Spatial Analysis Orchestrator (spec 094). What it cannot yet do is filter geographic features based on their own attribute values — area, height, construction year, land-use classification — or apply set-theoretic spatial relationships such as containment, intersection, and exclusion.

This feature adds **Spatial Query Analysis** as a new specialist registered with the Spatial Analysis Orchestrator. When a user asks "show me land parcels larger than 5,000 m² that intersect the flood zone," Lucy interprets the natural-language question, extracts the target dataset, attribute predicates, and spatial predicates, assembles a typed Query Bundle, dispatches it to the Spatial Query Specialist, and renders the matching features as an interactive map layer with per-feature attribute inspection.

---

## Clarifications

### Session 2026-11-06

- Q: Should the system apply a hard result cap for large queries, and if so what value? → A: No hard cap. Results are paginated and streamed to the map in batches. A user-visible warning is shown only when the total count is very large (threshold to be defined during planning), but no features are silently excluded.
- Q: What are the time bounds for first-batch delivery and query timeout? → A: The first batch of results must appear on the map within 3 seconds of query submission. Any query that has not completed within 30 seconds must be cancelled and the user notified with a clear explanation.
- Q: Is dataset access governed by user roles or permissions, or are all datasets available to all authenticated users? → A: All datasets in the Dataset Catalog are accessible to all authenticated users. Authentication is the only access gate; no per-dataset role or permission control is required for this feature.
- Q: Can OR logic mix attribute and spatial predicates together (e.g., "area > 5000 OR within zone"), or is OR scoped within each predicate type? → A: OR is scoped within each predicate type only. Attribute predicates may be OR'd together and spatial predicates may be OR'd together, but the two groups are always combined with AND. Mixed-type OR (one attribute, one spatial) is not supported in v1; users needing that result can run two separate queries.
- Q: What observability (logging and metrics) is required for query execution? → A: Full audit trail. Every query must be logged with the authenticated user identity, the full predicate set (all attribute and spatial conditions as submitted), the target dataset, the execution outcome (success / empty / error / timeout), duration, result count, and the IDs of all returned features. Logs must be retained for compliance review.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Attribute Filter Query (Priority: P1)

A user asks Lucy to find geographic features that satisfy one or more attribute conditions — for example, "Show me all buildings taller than 30 metres in the city centre" or "Find land parcels with an area greater than 5,000 m²." Lucy identifies the target dataset, extracts the attribute condition, executes the query, and renders the matching features on the map as a named, interactive result layer.

**Why this priority**: Attribute filtering is the most common form of spatial query and the minimum viable capability that unlocks practical use. Without it, users cannot interrogate geographic datasets at all.

**Independent Test**: Submit an attribute query with known test data and a verifiable condition; confirm that only features satisfying the condition are returned, rendered on the map, and that selecting any returned feature displays its attributes.

**Acceptance Scenarios**:

1. **Given** a geographic dataset containing buildings with height attributes, **When** the user asks for buildings taller than 30 m, **Then** Lucy returns only buildings that satisfy the condition and renders them as a highlighted result layer.
2. **Given** the user selects a result feature on the map, **When** the selection occurs, **Then** a panel displays the feature's key attributes (name, area, height, classification, or other relevant fields) without requiring a separate query.
3. **Given** no features satisfy the stated condition, **When** the query executes, **Then** Lucy informs the user no matching features were found and no empty layer is added to the map.
4. **Given** the user's attribute condition is ambiguous (e.g., the field name is not recognised), **When** Lucy cannot resolve it, **Then** Lucy asks a focused clarifying question before executing the query.

---

### User Story 2 - Spatial Relationship Query (Priority: P1)

A user asks Lucy to find features based on how they relate spatially to another feature or area — for example, "Which roads intersect the development zone?" or "Find buildings outside the flood risk area." Lucy identifies the target dataset, the reference feature or area, and the spatial relationship operator, then returns only features that satisfy the relationship.

**Why this priority**: Spatial relationships are what make this capability genuinely geographic. Attribute filters alone are available in any tabular tool; spatial predicates are the differentiating value.

**Independent Test**: With two known datasets in a known spatial arrangement, submit a relationship query and verify only features satisfying the stated relationship are returned, that features failing the relationship are absent, and that the result is rendered correctly.

**Acceptance Scenarios**:

1. **Given** a road network dataset and a defined development zone boundary, **When** the user asks for roads that intersect the development zone, **Then** only roads with geometry that crosses or touches the zone boundary are returned.
2. **Given** a buildings dataset and a flood risk area, **When** the user asks for buildings outside the flood risk area, **Then** buildings wholly outside the boundary are returned and buildings inside or on the boundary are excluded.
3. **Given** the reference feature cannot be resolved from the user's description or the current map context, **When** the query requires it, **Then** Lucy asks the user to specify or identify the reference feature before executing.
4. **Given** the user asks for features "near" a reference without specifying a distance, **When** Lucy cannot apply a default proximity rule, **Then** Lucy asks for the distance threshold rather than applying an arbitrary default.

---

### User Story 3 - Combined Attribute and Spatial Query (Priority: P1)

A user combines attribute and spatial conditions in a single question — for example, "Show me land parcels larger than 5,000 m² that fall within the designated development area" or "Find buildings constructed before 1980 that intersect the heritage conservation zone." Lucy decomposes the question into its attribute predicates and spatial predicates, applies both in a single coordinated query, and returns features that satisfy all conditions simultaneously.

**Why this priority**: Compound queries are the characteristic use case for spatial analysis. Users rarely ask one-dimensional questions; they need features that satisfy a combination of real-world criteria.

**Independent Test**: Configure a test dataset where only a subset of features satisfies both the attribute and spatial conditions independently; submit a combined query and verify the result contains only the intersection.

**Acceptance Scenarios**:

1. **Given** a parcel dataset with area attributes and a defined zone boundary, **When** the user asks for parcels larger than 5,000 m² within the zone, **Then** only parcels that satisfy both the area condition and the spatial containment are returned — parcels satisfying just one condition are excluded.
2. **Given** the question contains multiple attribute conditions joined by logical operators ("AND" / "OR"), **When** Lucy extracts the predicates, **Then** the logical structure is preserved and applied correctly within its predicate type: OR may join attribute predicates together or spatial predicates together, but the attribute group and spatial group are always AND'd. If the user phrases a mixed-type OR (e.g., "area > 5000 OR within zone"), **Then** Lucy explains the limitation and offers to run two separate queries.
3. **Given** the question contains a mix of attribute and spatial conditions, **When** Lucy is uncertain whether two conditions are meant to be combined or applied independently, **Then** Lucy restates its interpretation and asks for confirmation before running the query.

---

### User Story 4 - Clarification for Incomplete Queries (Priority: P2)

A user asks a spatial query that is incomplete, ambiguous, or refers to a dataset Lucy does not recognise. Lucy identifies the missing information, asks one focused question, and waits for the user's response before executing. Lucy does not guess at missing values or run a partial query and present results as complete.

**Why this priority**: Incorrect query results erode trust. It is better to ask one clear question than to silently apply an assumption that produces misleading results.

**Independent Test**: Submit a query that is missing the target dataset or a required reference feature; verify Lucy identifies exactly what is missing and asks a single focused question without executing the query.

**Acceptance Scenarios**:

1. **Given** the user asks for parcels matching a condition but does not specify a geographic scope, **When** Lucy cannot infer the scope from the current map view, **Then** Lucy asks for the geographic area before running the query.
2. **Given** the user names a dataset category Lucy does not recognise, **When** Lucy cannot match it to an available source, **Then** Lucy lists the datasets it does recognise in the current context and asks the user to choose.
3. **Given** Lucy has asked for clarification and the user provides a response, **When** Lucy has enough information to execute, **Then** the query runs and results are delivered without asking again.
4. **Given** a user provides a query with a numeric condition but no unit (e.g., "greater than 5000"), **When** the field's natural unit is unambiguous from context (e.g., m² for parcel area), **Then** Lucy applies the contextual unit and states which unit it used in its response.

---

### User Story 5 - Result Layer Management (Priority: P2)

Spatial query results appear as a named, independent layer on the map. The user can select individual result features to view their attributes, hide the layer, remove it, or clear all analysis layers. The map legend and layer list stay synchronized. The same behaviour is available in both 2D and 3D views.

**Why this priority**: Result layers that cannot be controlled accumulate and obscure the map, reducing utility. Per-feature attribute display turns a list of shapes into actionable information.

**Independent Test**: Run two successive spatial queries, verify each produces a named layer, select a feature from each layer to view its attributes, then hide one layer, remove the other, and confirm the map and legend update correctly without affecting the remaining layer.

**Acceptance Scenarios**:

1. **Given** a spatial query returns results, **When** the results are rendered, **Then** each query result occupies its own named layer and does not merge with layers from other queries.
2. **Given** a result layer is visible, **When** the user clicks a feature in that layer, **Then** a panel opens showing the feature's attributes; closing the panel does not alter the layer.
3. **Given** multiple result layers exist, **When** the user removes one layer, **Then** only that layer is removed and all other layers remain intact.
4. **Given** the user clears all analysis layers, **When** the action is confirmed, **Then** all query result layers are removed simultaneously and the legend no longer lists them.
5. **Given** the user switches between 2D and 3D views with result layers active, **When** the view changes, **Then** all layers and their visibility state are preserved.

---

### Edge Cases

- A query that returns a very large number of features (e.g., thousands of parcels) must stream results to the map in paginated batches rather than waiting for all features before rendering begins. The user must be informed of the total count. When the count exceeds a planning-defined warning threshold, a visible notice is shown; no features are silently excluded.
- If the queried dataset has no features with the requested attribute field, Lucy reports the field is unavailable rather than returning zero results without explanation.
- Attribute values with inconsistent units across records (e.g., some areas in m², others in hectares) must be normalised before comparison or the discrepancy must be surfaced.
- A query referencing a currently unavailable dataset must report the dataset as unavailable and not execute a partial query against a different dataset.
- If a spatial predicate produces a topologically degenerate result (e.g., intersection of two non-overlapping geometries), the system returns an empty result with an appropriate explanation.
- Queries involving large or complex polygon boundaries must not silently time out. If a query has not delivered its first batch within 3 seconds, a loading indicator must be shown. If execution has not completed within 30 seconds, the query is cancelled, any partial results are discarded, and the user is notified with a clear explanation; no partial result layer is left on the map.
- Switching the active map view region between query submission and result delivery does not cause results to be rendered in the wrong location.
- If the same query is submitted twice without clearing the first result, Lucy acknowledges the duplicate and asks whether to re-run or reuse the existing result.
- A query that attempts to OR an attribute predicate with a spatial predicate (mixed-type OR) is not supported. Lucy must explain the limitation and offer to run two separate queries to achieve the equivalent result.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Lucy MUST identify the target geographic dataset from the user's natural-language query without requiring users to know the dataset's technical name.
- **FR-002**: Lucy MUST support attribute-based filter conditions including numeric comparisons (greater than, less than, equal to, between), string matching (equals, contains, starts with), and date/year range comparisons.
- **FR-003**: Lucy MUST support spatial relationship predicates: **within**, **outside** (disjoint), **intersecting**, **containing**, and **near** (within a specified distance).
- **FR-004**: Lucy MUST support combining predicates with AND and OR logical operators, subject to the following rule: OR may only join predicates of the same type (attribute-to-attribute or spatial-to-spatial). The attribute predicate group and the spatial predicate group are always combined with AND. Mixed-type OR (one attribute predicate OR'd with one spatial predicate) is not supported and Lucy MUST tell the user to run two separate queries if that result is needed.
- **FR-005**: Lucy MUST ask a focused clarifying question when required information (target dataset, reference feature, geographic scope, or unit) is missing or ambiguous, and MUST NOT execute the query until the ambiguity is resolved.
- **FR-006**: Lucy MUST dispatch Spatial Query requests through the existing Spatial Analysis Orchestrator and Spatial Request Bundle architecture (spec 094), registering as a named specialist.
- **FR-007**: The system MUST execute the composed query against the spatial data backend and return matching features as structured spatial data including geometry and a defined set of display attributes. All datasets are accessible to any authenticated user; the system MUST verify authentication before executing any query but requires no per-dataset authorization check.
- **FR-008**: Matching features MUST be visualized on the existing 2D/3D map viewer as a highlighted feature layer with a user-readable name derived from the query.
- **FR-009**: Each spatial query result MUST occupy its own independent, named map layer that the user can hide, remove, or clear independently of other layers.
- **FR-010**: Selecting a result feature on the map MUST display the feature's key attributes in an accessible panel or tooltip without requiring a separate query.
- **FR-011**: Lucy MUST communicate the total number of matching features found. Results MUST be streamed and rendered in paginated batches so the map becomes useful before all features have loaded. When the total count exceeds a planning-defined warning threshold, a visible notice MUST be shown; no features may be silently excluded.
- **FR-012**: Lucy MUST surface a clear, user-facing explanation when a query returns no results, when a dataset is unavailable, or when execution fails — no silent failures.
- **FR-013**: Result layers MUST remain visible and interactive when the user switches between 2D and 3D map views.
- **FR-014**: The query interpretation logic MUST reuse Lucy's existing agent orchestration framework and MUST NOT introduce a parallel interpretation path outside it.
- **FR-015**: The system MUST produce a full audit log entry for every query executed, containing: authenticated user identity, the complete predicate set (all attribute and spatial conditions as interpreted), target dataset, execution outcome (success / empty / error / timeout), execution duration, result count, and the unique identifiers of all returned features. Audit logs MUST be retained in accordance with the platform's compliance retention policy and MUST NOT be deletable by end users.

### Key Entities

- **Spatial Query Bundle**: A structured, versioned representation of a user's query extending the Spatial Request Bundle (spec 094), containing: target dataset identifier, one or more attribute predicates (field, operator, value, unit), one or more spatial predicates (relationship operator, reference feature or geometry), the logical combinator (AND/OR) joining predicates, and the geographic scope (explicit or resolved from map context).
- **Attribute Predicate**: A single filter condition applied to a feature attribute — field name, comparison operator, value, and optional unit.
- **Spatial Predicate**: A single spatial relationship condition — relationship operator (within, outside, intersecting, containing, near), reference feature or geometry, and optional distance parameter with unit.
- **Query Result Feature**: A geographic feature returned by the query, containing its geometry, a unique identifier, and a predefined set of display attributes relevant to its dataset type.
- **Query Result Layer**: A named map layer generated from one query execution, containing all result features, with independent visibility and removal controls.
- **Dataset Catalog Entry**: A record describing an available geographic dataset — its user-facing name, supported attribute fields (name, type, unit), and the spatial relationship operators it supports.
- **Query Audit Entry**: An immutable log record created for every executed query, containing: user identity, full predicate set, target dataset, outcome, duration, result count, and the unique identifiers of all returned features. Retained for compliance review; not deletable by end users.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can submit a spatial query in natural language and see the first batch of matching features rendered on the map within 3 seconds of submission, without requiring any knowledge of the underlying data structure. The total feature count is always visible.
- **SC-002**: Users can combine at least one attribute condition and one spatial condition in a single question and receive a result that correctly reflects both conditions applied simultaneously.
- **SC-003**: When a query is incomplete, Lucy identifies exactly the missing information and asks for it without executing the query first; users can complete the interaction with a single follow-up response in the common case.
- **SC-004**: Each query result appears as a separately controllable map layer; users can hide, remove, or clear individual layers without affecting any other layer currently on the map.
- **SC-005**: Selecting any result feature on the map displays its attributes within two seconds in a panel without leaving the map view.
- **SC-006**: Results layers remain visible and correctly positioned when the user switches between 2D and 3D map views.
- **SC-007**: When a query produces no results, an error occurs, or a dataset is unavailable, users receive a clear explanation every time — no query failure is ever silent or unacknowledged.
- **SC-008**: A new dataset type can be made queryable by registering it in the Dataset Catalog without changing query interpretation or visualization logic.
- **SC-009**: Every query execution produces a complete, immutable audit log entry — including the full predicate set, the user identity, the outcome, the duration, and the IDs of all returned features — that is verifiable by an operator without access to application code.

---

## Assumptions

- The existing Spatial Analysis Orchestrator and Spatial Request Bundle architecture (spec 094) are in place and the Spatial Query Specialist can register with the orchestrator without changes to the coordinator's core dispatch logic.
- Geographic datasets are accessible through the existing spatial data backend; this feature does not introduce a new data-access layer but defines the query contract the backend must fulfill.
- The existing 2D/3D map viewer layer system (specs 050–051, 094) supports adding, hiding, and removing independently named feature layers without modification to the viewer core.
- The feature attribute panel reuses or extends the existing map feature inspection mechanism rather than introducing a new UI component from scratch.
- Dataset access control: all datasets in the Dataset Catalog are accessible to all authenticated users. No per-dataset role or permission control is in scope for this feature; authentication is the only access gate.
- The initial set of supported datasets includes land parcels, buildings, roads, and development areas; the Dataset Catalog is extensible without code changes to the query engine.
- The "near" spatial predicate requires the user to specify a distance; a default distance is not assumed.
- Performance targets: the first batch of results must appear within 3 seconds of query submission; any query not completing within 30 seconds is cancelled. These targets assume reasonable network conditions and a backend operating within normal load.
- Mobile-specific optimizations are out of scope for the initial delivery; the feature must work on desktop and tablet viewports.
- Internationalization of attribute field names and dataset names is out of scope for this specification but the architecture must not preclude it.
