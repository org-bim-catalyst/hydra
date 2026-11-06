# Feature Specification: Spatial Request Bundle & Analysis Orchestrator

**Feature Branch**: `[094-spatial-analysis-orchestrator]`

**Created**: 2026-11-06

**Status**: Draft

**Input**: User description: "Implement a unified spatial analysis framework that serves as the core engine for all geographic and POI-related requests. This feature should follow the architectural pattern of the Site Analysis Agent (Spec 057), acting as a coordinator for a team of specialized spatial agents. Instead of creating bespoke orchestration for every spatial tool, Lucy should dispatch all spatial requests to a single Spatial Analysis Agent. This agent identifies the required operation, coordinates one or more specialist agents (e.g., POI Discovery, Proximity Analysis, Boundary Resolution), and manages the delivery of structured results to the viewer. Coordination Engine: a Spatial Analysis Agent that interprets natural-language spatial requests and decomposes them into a Spatial Request Bundle (a structured set of operations, target features, and geographic constraints). Specialist Dispatch: a pluggable architecture where the coordinator can dispatch tasks to specialist agents via a parallel/merge workflow. Result Normalization: normalize outputs from various specialists into a consistent spatial result format the viewer can render. Viewer Integration: integrate with the existing 2D/3D viewer's layer and extension system to handle markers, buffers, and area highlights. Context Awareness: leverage the current map state (center, zoom, active boundaries) to resolve implicit location requests. Lifecycle Management: manage the request lifecycle from interpretation to execution to visualization to user-led layer clearing. Technical Constraints: reuse the Coordinator to Specialist pattern; orchestrator lives in the Application layer and specialist data retrieval in the Infrastructure layer; define a versioned Spatial Request Bundle contract that can be extended as new spatial specialists are added. Expected Outcome: a scalable, provider-neutral framework that allows new spatial capabilities to be added by registering a new specialist agent and a mapping in the coordinator, without changing the core orchestration or UI logic."

## Overview

Lucy can already answer some spatial questions — finding points of interest, and (per spec 093) measuring proximity — but each capability has grown its own interpretation path, its own result shape, and its own map-layer message. That does not scale: every new spatial capability multiplies bespoke code across the conversation layer, the backend, and the viewer.

This feature replaces those per-capability paths with **one spatial front door**: a Spatial Analysis Agent that Lucy dispatches every geographic and POI-related request to. The agent interprets the request, expresses it as a versioned **Spatial Request Bundle**, dispatches each operation to a registered specialist, merges the specialists' outputs into one normalized spatial result, and hands that result to the existing viewer layer system. Specialists never talk to the user and never touch the viewer directly; the coordinator is the only component that decides what reaches the user and the map.

The measure of success is not just that today's spatial questions work through the new path, but that tomorrow's capability — a new specialist — can be added by registration alone, with no change to the coordinator's flow or the viewer's rendering logic.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One coordinated answer for any spatial question (Priority: P1)

A user asks Lucy a geographic or POI question in natural language — for example, "Find hospitals in Muscat" or "Where are the schools around here?" Lucy does not pick a bespoke tool for that wording. It hands the request to the Spatial Analysis Agent, which works out what operation is being asked for, which locations and feature categories are involved, and which geographic constraints apply. The result appears as markers or highlights on the map, and Lucy explains in the conversation what was found.

**Why this priority**: This is the core value. Without a single entry point, every spatial capability remains a special case, and the platform cannot grow spatial features predictably.

**Independent Test**: Submit several spatial questions that previously took different paths and confirm each is handled by the same coordinator flow, produces the same normalized result shape, and renders on the map.

**Acceptance Scenarios**:

1. **Given** a user asks a supported spatial question with an explicit location and category, **When** the request is processed, **Then** the coordinator produces a bundle, dispatches the matching specialist, and the result is rendered as map layers and explained in chat.
2. **Given** the same user asks a spatial question that previously used a different capability path, **When** the request is processed, **Then** it goes through the same coordinator and produces the same normalized result structure.
3. **Given** a spatial request cannot be matched to any supported operation, **When** the coordinator interprets it, **Then** Lucy tells the user what it could not understand and what it can do, and no analysis is executed.

---

### User Story 2 - Several spatial operations in one request (Priority: P1)

A user asks for more than one thing at once — for example, "Find schools near hospitals and highlight the areas more than one kilometre from a clinic." The coordinator decomposes the request into a bundle with multiple operations, runs the independent specialists concurrently, and merges their outputs into one coherent result with distinct, named layers for each part.

**Why this priority**: Parallel dispatch and result merging are what make the coordinator worth having. A coordinator that runs one operation at a time would not justify replacing the existing paths.

**Independent Test**: Submit a request containing two independent operations and verify both specialists run, both results appear, each has its own layer, and the merged explanation identifies both parts.

**Acceptance Scenarios**:

1. **Given** a request contains two independent operations, **When** the bundle is dispatched, **Then** the operations are executed concurrently rather than one after another.
2. **Given** several specialists return results, **When** the coordinator merges them, **Then** every result keeps its own provenance and appears as a distinct named layer.
3. **Given** one operation depends on another's output (for example, a distance check that needs resolved features), **When** the bundle is dispatched, **Then** the dependent operation runs only after its input is available, and the user is not shown an intermediate incomplete result as final.

---

### User Story 3 - Implicit location from the current map view (Priority: P1)

A user who is already looking at an area asks "Find mosques here" or "Show me what is within this boundary," without naming a place. The coordinator uses the current map state — centre, zoom/extent, and any active boundary — to resolve the implicit location, and carries that resolved area into the bundle and the results.

**Why this priority**: Spatial questions are overwhelmingly about what the user is looking at. Ignoring map context forces users to repeat themselves and makes the feature feel disconnected from the viewer.

**Independent Test**: With a known map centre, extent, and active boundary, ask an implicit-location question and verify the analysis area matches the current view and the response states which area was used.

**Acceptance Scenarios**:

1. **Given** the viewer is focused on an area, **When** the user says "here" or "this area," **Then** the coordinator resolves the study area from the current view and states the resolved area in its response.
2. **Given** both an explicit location and map context exist, **When** the user names a place explicitly, **Then** the explicit location takes precedence over the current view.
3. **Given** no explicit location and no usable map context exists, **When** the request needs an area, **Then** Lucy asks a focused follow-up instead of assuming a location.
4. **Given** map context is provided by the viewer, **When** the coordinator uses it, **Then** the context is validated against the platform's supported extent and limits before any data retrieval.

---

### User Story 4 - Results are user-managed layers (Priority: P2)

Every analysis result becomes one or more clearly named layers on the map — markers, buffers, area highlights, or bands. The user can hide, remove, and clear layers, and the legend always matches what is visible. The same behaviour works in 2D and 3D.

**Why this priority**: Results that cannot be controlled become clutter, and users lose confidence in what the map is showing. Layer control also makes multi-operation results usable.

**Independent Test**: Run an analysis that produces multiple layers, then hide, remove, and clear them and verify both map contents and legend stay synchronized in both 2D and 3D.

**Acceptance Scenarios**:

1. **Given** an analysis produced several layers, **When** the user hides one, **Then** only that layer disappears and the legend reflects it.
2. **Given** layers from an earlier request are visible, **When** the user asks for a new analysis, **Then** Lucy asks whether to replace or keep the existing layers, unless the user has already said which.
3. **Given** the user clears an analysis, **When** the clear completes, **Then** all of that analysis's layers are gone while conversation history remains intact.
4. **Given** the viewer switches between 2D and 3D, **When** the mode changes, **Then** the active layers and their controls remain available.

---

### User Story 5 - Failures are visible and partial results survive (Priority: P2)

When one specialist cannot complete — a data source is unavailable, a category is unsupported, or processing fails — the other specialists' results are still delivered, and Lucy explains plainly what failed and what is missing. When everything fails, the user sees a clear failure, never an empty map presented as "no results."

**Why this priority**: A spatial result that silently omits a source is worse than an explicit partial result. This is the platform's no-silent-failure rule applied to spatial work.

**Independent Test**: Force one specialist to fail in a two-operation request and verify the successful result still renders while the failure is stated; force all to fail and verify a clear failure with no layers.

**Acceptance Scenarios**:

1. **Given** a multi-operation request where one specialist fails, **When** the results are merged, **Then** successful results are delivered and the failure is stated visibly.
2. **Given** a data source is unavailable, **When** the coordinator reports results, **Then** it identifies the missing coverage rather than presenting the result as complete.
3. **Given** every specialist fails, **When** the request ends, **Then** the user receives an explicit failure and no empty-success layers.
4. **Given** a retrieval succeeds with zero matches, **When** the result is reported, **Then** it is presented as a successful empty result, not as an error.

---

### User Story 6 - Adding a specialist without touching the core (Priority: P3)

A platform engineer adds a new spatial capability by registering a specialist for a new operation type. The coordinator's interpretation, dispatch, merge, and the viewer's rendering do not change.

**Why this priority**: This is the expected outcome that justifies the architecture. It is lower priority only because it is proven by the first two specialists, not by a third feature.

**Independent Test**: Add a test specialist through registration alone and verify it is interpreted, dispatched, normalized, and rendered without edits to coordinator flow or viewer rendering code.

**Acceptance Scenarios**:

1. **Given** a new specialist is registered for an operation the coordinator does not yet know, **When** a user requests that operation, **Then** the coordinator dispatches it and its result renders through the existing layer system.
2. **Given** a new specialist is added, **When** the existing specialists' requests are re-run, **Then** their behaviour is unchanged.

---

### Edge Cases

- **No supported operation matches the request.** No analysis runs; Lucy explains what it understood and what it supports.
- **The request mixes supported and unsupported operations.** Supported operations run; unsupported ones are named as not performed — the whole request is not silently abandoned or silently completed.
- **The location is ambiguous.** Several places match the name. Lucy asks the user to choose; no analysis runs against a guessed location.
- **The location cannot be resolved at all.** No analysis runs; the failure is stated in the same turn.
- **The study area is missing for an area-based operation.** Lucy asks for it; it never defaults to an unbounded region.
- **The study area exceeds supported limits.** Lucy explains the limit and asks for a smaller area; nothing is retrieved outside authorized bounds.
- **One specialist times out or is cancelled.** Other specialists' results are unaffected; the timed-out operation is reported as not completed.
- **The user clears layers while a request is still running.** Layers already shown are cleared; the running request still ends with one visible outcome, and any late result is not silently dropped — it is either presented as a new result or explicitly cancelled.
- **The user asks the same thing twice in a row.** The second request is treated as a new request; existing layers are kept or replaced according to the clear-or-keep answer.
- **Results fall outside the confirmed area.** Out-of-area features are excluded; if clipping occurred, the result says so.
- **The same feature is returned by two specialists.** It is de-duplicated by stable source identity when available; visually coincident but distinct features are not merged.
- **The page is reloaded.** Layers are session-scoped and do not survive a reload; the conversation remains, and the user can re-run the request. (See Assumptions.)
- **The viewer is in 3D.** Markers, buffers, and area highlights render with equivalent meaning and stay controllable.
- **The provider is unavailable.** Explicit coverage/unavailability is reported; an empty success is never fabricated.

## Requirements *(mandatory)*

### Functional Requirements

**Coordination**

- **FR-001**: Lucy MUST route every geographic, POI, and spatial analysis request through a single Spatial Analysis Agent (the coordinator); no spatial capability may be invoked through a separate bespoke orchestration path.
- **FR-002**: The coordinator MUST interpret a natural-language spatial request into a Spatial Request Bundle containing one or more operations, the target features or categories, the geographic constraints (including resolved study area), and any user-expressed preferences such as replace-versus-keep layers.
- **FR-003**: The Spatial Request Bundle MUST be a versioned contract; a request carrying an unsupported version MUST be rejected with a clear message rather than misinterpreted.
- **FR-004**: The coordinator MUST validate a bundle before execution; an invalid or incomplete bundle MUST produce a user-visible explanation and MUST NOT execute partially valid operations as if complete.
- **FR-005**: The coordinator MUST ask a focused follow-up when a required input (location, feature category, distance, or study area) is missing or materially ambiguous, using established conversation and map context first.

**Specialist dispatch**

- **FR-006**: The coordinator MUST dispatch each bundle operation to a registered specialist through a common specialist contract, and specialists MUST be registrable without editing the coordinator's interpretation, dispatch, merge, or delivery logic.
- **FR-007**: Independent operations MUST execute concurrently; a request MUST NOT take the sum of independent operation durations when it can take the longest.
- **FR-008**: An operation that depends on another operation's output MUST execute after that output is available, and intermediate incomplete output MUST NOT be presented to the user as a final result.
- **FR-009**: A specialist MUST NOT address the user, push a chat message, or add a viewer layer directly; all user-visible delivery MUST pass through the coordinator.
- **FR-010**: Every specialist operation MUST run under the requesting user's authorization and within the confirmed study area; no operation may widen the user's data access.

**Result normalization and merging**

- **FR-011**: The coordinator MUST normalize every specialist's output into one common spatial result format containing the resolved request, status, features/geometries, counts, data-source coverage, warnings, and provenance.
- **FR-012**: When specialists return results for the same request, the coordinator MUST merge them into a single result while preserving each operation's provenance and coverage information.
- **FR-013**: The merged result MUST distinguish a successful empty result from a failure; an empty result MUST NOT be produced when retrieval failed.
- **FR-014**: The merged result MUST carry user-readable data-coverage and limitation information whenever a required source is unavailable, partial, or clipped.

**Context awareness**

- **FR-015**: The coordinator MUST accept the current map context — centre, extent/zoom, and any active boundary — and use it to resolve implicit location references.
- **FR-016**: An explicitly stated location MUST take precedence over map context; if neither yields a valid location for an operation that requires one, the coordinator MUST ask.
- **FR-017**: Map context supplied by the viewer MUST be validated against supported extents and limits before data retrieval and MUST be treated as untrusted input.

**Viewer integration and lifecycle**

- **FR-018**: The system MUST render normalized results through the existing viewer layer and extension system as distinct, user-named layers, supporting markers, buffers, and area highlights in both 2D and 3D.
- **FR-019**: Layer names, legends, and map contents MUST remain synchronized at all times.
- **FR-020**: Users MUST be able to show or hide a layer, remove a single layer, and clear all layers belonging to an analysis.
- **FR-021**: When a new analysis would replace layers already displayed, the system MUST ask the user whether to replace or keep them, unless the user has already expressed that preference.
- **FR-022**: Every request MUST end in exactly one user-visible outcome: a result summary, a clarification request, or a failure. A request MUST NOT end silently.
- **FR-023**: Results and layer state MUST remain consistent across a 2D/3D switch.
- **FR-024**: Clearing layers MUST be user-initiated and MUST NOT alter conversation history.

**Failure, cancellation, and observability**

- **FR-025**: A specialist failure MUST NOT prevent other successful results from being delivered, and MUST be stated visibly to the user with the affected operation identified.
- **FR-026**: Cancellation MUST propagate to in-flight specialists; cancelled work MUST NOT deliver late results or leave a request without its single ending outcome.
- **FR-027**: Every failure MUST be recorded with context that identifies the request, operation, and specialist, so it is diagnosable from operational logs alone — never swallowed, logged-only without a user-visible outcome, or discarded.

**Extensibility**

- **FR-028**: Adding a new spatial specialist, including its operation type and result mapping, MUST NOT require changes to the coordinator's core flow or to viewer rendering logic; registration and a mapping entry MUST be sufficient (subject to the operation having a viewer representation already supported).
- **FR-029**: The existing POI discovery capability and the proximity analysis capability specified in spec 093 MUST be expressible as specialists under this coordinator without duplicating their data-retrieval logic.

### Key Entities *(include if feature involves data)*

- **Spatial Request Bundle**: The versioned, structured interpretation of one user request; owns one or more operations, target features/categories, geographic constraints, map context, and user preferences.
- **Spatial Operation**: One unit of work inside a bundle; identifies the operation type, its targets, its distance or area parameters, its study area, and its dependencies on other operations.
- **Specialist (Specialist Agent)**: A registered capability that fulfils one operation type; produces a normalized result and never delivers to the user directly.
- **Spatial Analysis Result**: The coordinator's normalized output; contains the resolved request, status, features/geometries, counts, coverage, warnings, and per-operation provenance.
- **Spatial Layer**: A viewer-session representation of part of a result (markers, buffer, area highlight, band); named, styled, visible/hidden, and owned by one analysis.
- **Map Context**: The viewer state supplied with a request — centre, extent/zoom, active boundary — used to resolve implicit locations and validated before use.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of supported spatial requests are dispatched through the coordinator; verification finds zero remaining bespoke spatial orchestration paths.
- **SC-002**: At least 95% of successful, unambiguous spatial requests make their results visible on the map within 10 seconds, excluding time spent waiting for user clarification.
- **SC-003**: A request containing two independent operations completes in at most 75% of the time the same two operations take when run sequentially, demonstrating concurrent dispatch.
- **SC-004**: 100% of requests end with exactly one user-visible outcome — result, clarification, or failure — with no silent endings.
- **SC-005**: When one specialist of several fails, 100% of successful results are still delivered and the failure is visible; when all fail, 100% show an explicit failure and produce no empty-success layers.
- **SC-006**: 100% of delivered results state their data source and any coverage limitation; no result with partial coverage is presented as complete.
- **SC-007**: In acceptance tests, at least 90% of implicit-location requests resolve to the correct current-map area, and 100% of ambiguous cases ask for clarification rather than guessing.
- **SC-008**: Users can hide, remove, and clear layers with the legend synchronized in 100% of tested 2D and 3D flows, including after a mode switch.
- **SC-009**: A platform engineer can add a new specialist and have it interpreted, dispatched, normalized, and rendered without modifying coordinator flow or viewer rendering — verified by adding one.
- **SC-010**: 100% of failures are attributable from operational logs to the request, operation, and specialist that caused them.

## Assumptions

- The existing map viewer (2D/3D) and its layer/extension system are reused; this feature does not introduce a second viewer or layer manager.
- The current map context is supplied by the client viewer with each spatial request; the server treats it as untrusted input and validates it.
- Spatial data retrieval continues to use the platform's existing providers (including location resolution and POI/data retrieval); this feature adds no new external data provider.
- The existing POI discovery capability is adopted as the first specialist; the proximity analysis capability from spec 093 is the second and is expected to register through the same seam. This feature does not re-implement either capability's retrieval logic.
- Boundary resolution (spec 042) supplies or validates study areas where an area is required.
- Spatial results and layers are session-scoped for this release; no new durable server-side storage is introduced, consistent with spec 093's research. Conversation history is unaffected by layer clearing.
- The coordinator runs within the existing conversational request path, so results can be delivered in the same chat turn; it does not require a new background job or push channel.
- Existing authorization, rate limiting, and provider quota rules continue to apply to every specialist operation.
- Adding a specialist for an operation whose viewer representation is genuinely new (for example, a new geometry type) may still require viewer work; the extensibility guarantee in FR-028 covers operations that map onto the supported marker/buffer/area-highlight representations.
