# Feature Specification: Location Discovery and Viewer

**Feature Branch**: `035-location-discovery-viewer`

**Created**: 2026-08-23

**Status**: Draft

**Input**: User description: "Implement a new Ask Lucy capability for location discovery and visualization."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Unique Location Name Resolves Automatically (Priority: P1)

A user asks Lucy to find a location by name (e.g., "Find Al Safa 2 Park"). Lucy's agentic system searches available geospatial sources and finds exactly one confident match. Lucy confirms the resolved location to the user and automatically centers the existing Three.js viewer on it.

**Why this priority**: This is the primary happy-path flow and represents the highest-value interaction. Users should be able to discover and visualize locations entirely through natural language without ever providing coordinates.

**Independent Test**: Can be fully tested by typing a unique location name into the chat and verifying that the Three.js viewer centers on the correct location without any additional user action.

**Acceptance Scenarios**:

1. **Given** the user types "Find Al Safa 2 Park", **When** Lucy's agentic resolution returns a single confident match, **Then** Lucy confirms the location name to the user and the Three.js viewer centers and frames on that location.
2. **Given** a unique location name is submitted, **When** the agentic search returns a result with high confidence, **Then** no disambiguation list is shown and the viewer loads automatically.
3. **Given** location resolution is in progress, **When** the search is running, **Then** a visible loading indicator is shown in the chat interface.

---

### User Story 2 - Ambiguous Name Presents a Selection List (Priority: P2)

A user asks Lucy to find a location name that matches several real places (e.g., "Show Central Park"). Lucy does not pick one arbitrarily. Instead, Lucy presents a selectable list of all matching locations, each with enough detail to distinguish them (name, city/district, country, coordinates where useful). Once the user selects one, Lucy loads it into the viewer.

**Why this priority**: Silently auto-selecting the wrong location would be a trust-breaking failure, especially for geospatial analysis tasks. Disambiguation must be explicit and user-driven.

**Independent Test**: Can be tested by submitting a well-known ambiguous place name and verifying that a selection list appears, that no location is loaded until the user picks one, and that the viewer correctly loads the chosen match.

**Acceptance Scenarios**:

1. **Given** the user provides a location name with multiple geospatial matches, **When** the agentic search returns two or more candidates, **Then** Lucy displays a list showing each candidate's name, city/district, country, and available coordinates.
2. **Given** a disambiguation list is shown, **When** the user selects a specific entry, **Then** the Three.js viewer centers on the selected location and the selection list is dismissed.
3. **Given** a disambiguation list is shown, **When** the user takes no action, **Then** no location is loaded into the viewer and no default selection is made.
4. **Given** the agentic search returns a result below the confidence threshold for automatic selection, **Then** it is treated as ambiguous and added to the disambiguation list rather than auto-loaded.

---

### User Story 3 - Unresolvable Name Falls Back to Coordinate Input (Priority: P2)

When Lucy cannot confidently resolve a location by name, Lucy tells the user clearly and asks them to provide geographic coordinates. The user enters latitude and longitude. Lucy validates the coordinates and, if valid, loads the location into the viewer. Invalid coordinates produce a clear inline error.

**Why this priority**: Users may attempt to find obscure or incorrectly spelled locations. The system must degrade gracefully rather than silently failing or leaving the user stuck.

**Independent Test**: Can be tested by submitting a nonsense or unrecognized location name, observing the fallback prompt, entering both valid and invalid coordinate pairs, and verifying that only valid coordinates result in a viewer update.

**Acceptance Scenarios**:

1. **Given** the user provides a location name that yields no results, **When** Lucy completes the agentic search, **Then** Lucy informs the user the location was not found and prompts for latitude/longitude coordinates.
2. **Given** the user provides valid latitude/longitude coordinates, **When** Lucy receives them, **Then** Lucy validates them and loads the location into the Three.js viewer.
3. **Given** the user provides coordinates outside the valid range (latitude outside −90 to 90, longitude outside −180 to 180), **When** Lucy processes them, **Then** Lucy displays a clear validation error message and does not load anything into the viewer.
4. **Given** the user provides coordinates in an unrecognized format, **When** Lucy processes them, **Then** Lucy explains the expected format and invites the user to try again.

---

### User Story 4 - Resolved Location Becomes Active Site Context (Priority: P2)

After a location is successfully loaded into the Three.js viewer — whether found by name or entered as coordinates — it is preserved as the active site context for the current session. Subsequent Ask Lucy analysis workflows (e.g., urban design queries, site assessments) reference this active location without the user having to re-specify it.

**Why this priority**: Location context is foundational for downstream BIM and geospatial analysis tasks. Without persistence, users must repeat location entry every time they switch analytical tasks, breaking workflow continuity.

**Independent Test**: Can be tested by loading a location, then invoking a subsequent Lucy analysis workflow, and verifying that the workflow references the same location without prompting the user to re-enter it.

**Acceptance Scenarios**:

1. **Given** a location has been successfully loaded into the viewer, **When** the user initiates a subsequent analysis in the same session, **Then** the analysis uses the active location without requiring the user to re-specify it.
2. **Given** the user loads a second location, **When** it is confirmed, **Then** it replaces the previous active location and downstream analyses use the new one.
3. **Given** a location is loaded by coordinate input, **When** the user then starts an analysis, **Then** the analysis uses the coordinate-specified location as the active site.

---

### Edge Cases

- What happens when the geospatial source is temporarily unavailable or times out?
- How does the system handle a location name that is valid but returns no geometry (only a point coordinate)?
- What if the user enters coordinates using commas as decimal separators (locale-specific formats)?
- What if the user submits the same location name twice consecutively?
- How does the system behave if the Three.js viewer is not yet initialized when the location is resolved?
- What if the location name contains special characters or non-Latin scripts?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST resolve location names through an agentic process that queries available geospatial or map data sources, rather than relying on a hard-coded list.
- **FR-002**: System MUST define and use a normalized location model containing at minimum: `name`, `latitude`, `longitude`, `source`, `confidence`, and optional administrative metadata (city, district, country, location type, bounding box).
- **FR-003**: When exactly one location is found with sufficient confidence (as defined by a fixed system-level threshold, not user or operator configurable in v1), system MUST automatically center and frame the existing Three.js viewer on that location and confirm the match to the user.
- **FR-004**: When multiple candidate locations are found, or when a single result falls below the fixed confidence threshold, system MUST present a selectable disambiguation list and wait for user selection before loading the viewer. The list MUST show up to 10 results initially; if more than 10 candidates exist, a "Show more" control MUST be available to reveal additional results on demand. The list MUST show up to 10 results initially; if more than 10 candidates exist, a "Show more" control MUST be available to reveal additional results on demand.
- **FR-005**: System MUST NEVER auto-select one location from a set of multiple plausible matches.
- **FR-006**: Each entry in the disambiguation list MUST include at minimum: location name, city/district, country, and available coordinate information.
- **FR-007**: When no confident match is found, system MUST inform the user and request latitude/longitude coordinates as a fallback.
- **FR-008**: System MUST validate user-provided coordinates: latitude must be between −90 and 90 inclusive; longitude must be between −180 and 180 inclusive.
- **FR-009**: Invalid coordinates MUST produce a clear, user-visible validation message within the chat interface; no viewer update may occur until coordinates are valid.
- **FR-010**: The resolved or user-specified location MUST be persisted as the active site context for the current session, making it available to subsequent Ask Lucy analysis workflows without re-entry.
- **FR-011**: Location discovery/resolution MUST be separated from Three.js visualization so the viewer consumes only the normalized location model regardless of how the location was found.
- **FR-012**: The feature MUST reuse the existing Three.js viewer architecture; a separate or duplicate viewer MUST NOT be created.
- **FR-013**: All intermediate states — loading, resolved, multiple matches, not found, error — MUST be surfaced with appropriate UI feedback (loading indicators, inline messages, or error states) in the chat interface.
- **FR-014**: The feature MUST maintain existing UI/UX style and interaction patterns of the Ask Lucy interface.
- **FR-015**: Geospatial source outages or timeouts MUST surface a user-visible error message rather than producing a silent failure. The maximum wait time for a geospatial search response is **15 seconds**; if no response is received within this window, the system MUST present a timeout error and offer the user the coordinate-input fallback.
- **FR-017**: The system MUST cache recent geocoding results (keyed by normalized query string) so that a repeated or rate-limited query can be served from cache without a live API call. A user-visible error MUST be shown only when both the live API call fails/is rate-limited AND no cached result exists for the query.
- **FR-016**: When a location is successfully resolved and loaded, the `ResolvedLocation` data (name, latitude, longitude, source, confidence, and available metadata) MUST be stored as a structured typed payload linked to the chat message record, enabling location data to be retrieved from chat history without reparsing response text.

### Key Entities

- **ResolvedLocation**: The normalized location model representing a confirmed site. Contains: `name`, `latitude`, `longitude`, `source` (which geospatial service returned it), `confidence` (numeric or categorical confidence score), and optional fields: `city`, `district`, `country`, `locationType`, `boundingBox`. Stored as a structured typed payload linked to the chat message that triggered the resolution.
- **LocationCandidate**: A single result returned during agentic search before disambiguation. Carries enough information to display in a selection list and to be promoted to a `ResolvedLocation` on user selection.
- **ActiveSiteContext**: The session-level record of the currently selected location, referenced by downstream analysis workflows. Updated whenever a new location is successfully loaded.
- **GeocodingCache**: A short-lived cache of recent geocoding query results, keyed by normalized query string. Serves cached candidates when the live API is rate-limited or temporarily unavailable, reducing user-visible failures under transient API constraints.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can say a location name in natural language and have it loaded into the viewer within the same conversation turn, without providing coordinates, for locations with a unique confident match.
- **SC-002**: When multiple location matches exist, users can identify and select the correct one without leaving the chat interface or performing external lookups.
- **SC-003**: When a location cannot be found by name, the coordinate fallback flow is presented in the same conversation turn, with no dead-end or silent failure.
- **SC-004**: Invalid coordinate inputs produce a user-visible validation message within the same interaction, with a clear explanation of what was wrong and how to correct it.
- **SC-005**: After a location is loaded, subsequent analysis flows within the same session use that location without requiring the user to re-specify it.
- **SC-006**: No ambiguous or low-confidence location is ever automatically loaded into the viewer without explicit user selection.
- **SC-007**: All loading, error, and selection states are visible to the user — no state transition is silent or unacknowledged.
- **SC-008**: The agentic geospatial search completes within 15 seconds under normal network conditions; if it does not, a user-visible timeout error is shown and the coordinate-input fallback is offered.

## Clarifications

### Session 2026-08-23

- Q: How many results should the disambiguation list show, and is pagination/reveal needed? → A: Show up to 10 matches initially; a "Show more" control reveals additional results if more than 10 candidates exist.
- Q: How is the resolved location stored in relation to the chat message? → A: As a structured typed payload (`ResolvedLocation`) attached to the chat message record, not embedded only in response text.
- Q: What is the maximum wait time for agentic geospatial search before a timeout error is shown? → A: 15 seconds.
- Q: How should the system handle geocoding API rate-limit rejections? → A: Cache recent geocoding results; serve from cache on rate-limit hit; surface a user-visible error only when both the live API and the cache fail to return a result.
- Q: Should the confidence threshold (single auto-load vs. disambiguation) be operator-configurable or a fixed system constant? → A: Fixed system constant for v1; no operator or user control. Can be promoted to configurable in a future iteration after real usage data informs the right default.

## Assumptions

- The existing Three.js viewer is capable of accepting a normalized location object (latitude, longitude, and optional bounding box) and repositioning/reframing accordingly; no viewer-internal changes to this contract are required.
- Lucy's agentic system has access to at least one openly available geospatial search/geocoding data source (e.g., OpenStreetMap Nominatim or equivalent) that does not require per-user authentication.
- The geospatial search source returns structured results including name, coordinates, administrative hierarchy, and a relevance or confidence signal sufficient to distinguish single confident matches from ambiguous ones.
- The active site context is session-scoped for v1; persistence across sessions or cross-user sharing is out of scope.
- The implementation follows existing agentic tool patterns in the codebase; no new agentic infrastructure is needed, only a new geospatial search tool definition.
- Mobile/responsive behavior follows the same patterns as the existing Three.js viewer; no separate mobile layout is required.
- Location name inputs do not need to support structured query syntax (e.g., "name:X country:Y"); plain natural-language names are the primary input format.
- Coordinate input is expected in decimal degrees (e.g., 25.2048, 55.2708); DMS (degrees/minutes/seconds) format is out of scope for v1.
- The confidence threshold that separates "auto-load" from "disambiguation" is a fixed system constant for v1; making it operator- or user-configurable is explicitly out of scope and deferred to a future iteration.
