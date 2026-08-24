# Feature Specification: POI Viewer Zoom & Focus

**Feature Branch**: `038-viewer-poi-zoom`

**Created**: 2026-08-24

**Status**: Draft

**Input**: User description: "When user names a location/POI in chat, the viewer must zoom in to an appropriate level to show the place clearly. Lucy must stop saying she can't interact with the map — she should confidently execute viewer commands. Zoom and focus requests must be honoured."

## Clarifications

### Session 2026-08-24

- Q: What form should the zoom information take in the ConfirmedLocationData payload? → A: Bounding box (northeast + southwest lat/lng) — viewer fits the box to its own viewport dimensions and derives the camera altitude client-side.
- Q: When the geocoding result has no bounding box, how should the viewer determine zoom? → A: Fall back to a fixed altitude per location_type — ROOFTOP/RANGE_INTERPOLATED → street level (~200 m), GEOMETRIC_CENTER → block level (~800 m), APPROXIMATE → city level (~8 000 m).
- Q: What visual form should the POI marker take? → A: All styles are available in a viewer control panel so users can swap between them; pulsing ring/halo at ground level with floating label is the default. Available styles: pulsing ring (default), classic pin, 3D extruded highlight, simple dot.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Automatic POI Zoom on Location Resolve (Priority: P1)

When a user names a specific place — a building, landmark, park, or facility — Lucy resolves the location and the viewer both centres on it **and** zooms to an altitude that makes that place clearly visible without requiring any additional request from the user.

Currently the viewer centres on coordinates but stays at whatever altitude it was at, so a building requested by name is a single pixel in a city-level view. This story fixes the fundamental usability gap: ask for a place, see that place.

**Why this priority**: This is the core broken experience. Every location query is currently unsatisfying because the zoom does not follow the place type. Fixing this alone makes location queries genuinely useful.

**Independent Test**: Send "Show me Dubai Mall" in chat. Without any follow-up message, the viewer should re-centre AND zoom to street/block level so the mall footprint occupies a meaningful portion of the viewport.

**Acceptance Scenarios**:

1. **Given** the viewer is at country-level zoom, **When** the user says "Take me to Dubai Mall", **Then** the viewer centres on Dubai Mall's coordinates AND zooms to a level that makes the mall's footprint clearly visible (approximately street or block level).
2. **Given** Lucy resolves a city (e.g., "Show me Dubai"), **When** the location is confirmed, **Then** the viewer zooms to city level — wide enough to show the city outline but not the whole country.
3. **Given** Lucy resolves a country or region (e.g., "Show me the UAE"), **When** the location is confirmed, **Then** the viewer zooms to country/region level, not street level.
4. **Given** the geocoding service returns bounding-box information for a place, **When** the viewer receives the location, **Then** the zoom is derived from the bounding box so the full extent of the place fits inside the viewport.

---

### User Story 2 — Explicit Zoom / Focus Commands (Priority: P2)

After navigating to a location, a user can issue natural-language zoom commands such as "zoom in", "zoom out", "get closer", "pull back", "fly to it", or "focus on it". Lucy executes these as viewer actions and confirms them concisely — she does **not** explain how to zoom using Google Maps or say she lacks the capability.

**Why this priority**: Lucy currently replies "I don't have the capability to zoom" and then offers Google Maps instructions, which destroys trust and makes the feature feel broken. Once P1 is done, this story unlocks interactive refinement of the view.

**Independent Test**: After navigating to any location, send "zoom in". The viewer must zoom in by at least one meaningful level and Lucy's reply must confirm the action rather than disclaim it.

**Acceptance Scenarios**:

1. **Given** a location is active in the viewer, **When** the user says "zoom in" (or "get closer", "fly closer", "focus on it"), **Then** the viewer zooms in by one meaningful level and Lucy replies with a short confirmation (e.g., "Done — zoomed in on Dubai Mall.").
2. **Given** a location is active in the viewer, **When** the user says "zoom out" (or "pull back", "wider view", "show more"), **Then** the viewer zooms out by one meaningful level and Lucy confirms.
3. **Given** no active location exists, **When** the user says "zoom in", **Then** Lucy responds that there is no active location to zoom into and suggests naming a place first — she does not explain how to use Google Maps.
4. **Given** Lucy is asked to zoom, **When** she replies, **Then** her reply is brief (one sentence confirming action) and never contains instructions about third-party map services.

---

### User Story 3 — Accurate POI Display Name (Priority: P3)

When Lucy resolves a location, the place name displayed in the viewer and spoken/written by Lucy matches what the user asked for — or the canonical, widely-recognised name of that place — not an unrelated adjacent landmark returned by the geocoding service.

In the observed failure, asking for "Dubai Mall" displayed "Burj Khalifa - Downtown Dubai" because the geocoding result's formatted address described the surrounding area. The viewer and Lucy's confirmation should say "Dubai Mall", not "Burj Khalifa".

**Why this priority**: Correctness of the displayed name matters for trust but does not block the zoom functionality. P1 and P2 deliver the working zoom experience; P3 makes the naming match user expectations.

**Independent Test**: Say "Take me to Dubai Mall". The viewer's location label and Lucy's confirmation must include "Dubai Mall", not substitute another landmark's name.

**Acceptance Scenarios**:

1. **Given** the user asks for "Dubai Mall", **When** Lucy confirms and the viewer updates, **Then** both the confirmation text and the viewer label include "Dubai Mall" — the user's requested name is preserved as the display name.
2. **Given** the geocoding service returns a formatted address that differs from the user's query (e.g., describes an area rather than the specific POI), **When** Lucy confirms, **Then** the user's original place name (or the closest recognised canonical name for that POI) is used as the primary label, with the geocoding address used only as supplementary context.
3. **Given** a place name is genuinely ambiguous (e.g., the geocoding service cannot resolve it closer than a neighbourhood), **When** Lucy confirms, **Then** Lucy states what was resolved ("I've centred on Downtown Dubai — I couldn't pinpoint a specific Dubai Mall building") rather than silently substituting a different place name.

---

### Edge Cases

- What happens when the user asks for a very small place (a specific room, a floor of a building) that the geocoding service cannot resolve more precisely than a city block?
- What happens when the zoom command is issued but the viewer is already at maximum or minimum zoom?
- What happens when a zoom command arrives while the viewer is mid-animation from a previous command?
- What if the bounding box returned by the geocoding service is extremely large (e.g., a country) but the user expects street-level zoom because they named a specific building?
- What if the user asks "zoom in 5 times" — should incremental commands be chained, or is one level per command the behaviour?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When the viewer receives a confirmed location, it MUST zoom to an altitude appropriate for the resolved place's type and bounding box, not remain at the current zoom level.
- **FR-002**: The confirmed location payload MUST carry the geocoding viewport as a bounding box (northeast lat/lng + southwest lat/lng). The viewer fits this box to its own pixel dimensions to derive the correct camera altitude — the server does not pre-compute a zoom level or altitude.
- **FR-002a**: When the geocoding result carries no bounding box, the viewer MUST fall back to a fixed altitude derived from the result's location precision: ROOFTOP or RANGE_INTERPOLATED → street level (~200 m); GEOMETRIC_CENTER → block level (~800 m); APPROXIMATE → city level (~8 000 m).
- **FR-003**: Lucy MUST respond to zoom-direction commands ("zoom in", "zoom out", "get closer", "pull back", "fly to it", "focus on it") by emitting a viewer zoom instruction and confirming the action.
- **FR-004**: Lucy's replies to location and zoom requests MUST be brief, action-confirming, and never reference third-party map services (Google Maps, Apple Maps, etc.) as alternatives to a capability she cannot perform.
- **FR-005**: The display name for a confirmed location MUST be derived from the user's original query or the geocoding service's place name for that specific POI — not from surrounding area address fields that describe a different landmark.
- **FR-006**: When zooming in response to an explicit user request, the viewer MUST animate smoothly to the new altitude rather than cutting instantly.
- **FR-007**: When a zoom command is received but no active location exists, Lucy MUST inform the user and prompt them to name a place — she MUST NOT disclaim inability.
- **FR-008**: The zoom level for a confirmed location MUST be proportional to the place type: a specific building resolves to street/block level; a district or neighbourhood to block/area level; a city to city level; a country or region to country level.
- **FR-009**: Zoom commands issued while the viewer is mid-animation MUST queue or cancel the current animation gracefully, not produce visual glitches.
- **FR-010**: When a location is confirmed, a visual marker MUST be placed at the resolved coordinates so the user can see exactly which building or site Lucy identified, even when multiple buildings are visible at the zoomed-in level.
- **FR-011**: The marker MUST be rendered as part of the 3D viewer scene — positioned at ground level at the POI coordinates — so it aligns correctly with the 3D map regardless of camera angle or tilt.
- **FR-012**: The marker MUST display the POI display name (FR-005) as a floating label and be visually distinctive enough to stand out from surrounding geometry. The default style is a pulsing ring/halo at ground level with a label floating above it.
- **FR-012a**: The viewer control panel MUST offer a marker style selector listing all available styles — pulsing ring (default), classic pin, 3D extruded highlight, and simple dot — so users can swap between them at any time without reissuing a location query.
- **FR-012b**: The user's chosen marker style MUST persist for their session; the pulsing ring is the fallback when no preference has been set.
- **FR-013**: The marker MUST be removed or replaced when a new location is confirmed, so only one active POI marker exists at a time.

### Key Entities

- **ConfirmedLocationData**: The payload emitted by the agent when a location is resolved. Currently carries lat/lon and a display name. Extended to also carry the geocoding viewport as a bounding box (northeast lat/lng + southwest lat/lng) so the viewer can fit the place into its own viewport without the server guessing screen dimensions.
- **ViewerZoomCommand**: A viewer-control instruction (separate from location resolution) that adjusts the camera altitude/zoom without changing the centred location.
- **PlaceDisplayName**: The human-readable label shown in the viewer and used by Lucy. Derived from the user's query or the POI's canonical name, not from a raw geocoding address string.
- **POIMarker**: A visual 3D marker placed at the confirmed location's coordinates in the viewer scene. Carries the display name label, persists until replaced by a new location confirmation, and is rendered as part of the 3D scene geometry so it tracks correctly with camera movement. Rendered in the user's currently selected marker style.
- **MarkerStyle**: The user's chosen visual style for the POI marker. One of: pulsing ring (default), classic pin, 3D extruded highlight, simple dot. Stored as a viewer preference; falls back to pulsing ring when unset.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of location resolutions that return a bounding box result in the viewer zooming to fit that bounding box within the viewport, with no additional user action required.
- **SC-002**: Explicit zoom commands ("zoom in", "zoom out", and at least four natural-language equivalents per direction) are recognised and executed correctly in 100% of cases when an active location exists.
- **SC-003**: Lucy's replies to location and zoom requests contain zero references to third-party mapping services in 100% of cases.
- **SC-004**: The display name shown after asking for a named POI matches the user's requested name or the canonical POI name in at least 95% of test cases across 20 distinct Dubai landmarks.
- **SC-005**: Zoom animations complete within 1.5 seconds for a single zoom step on the target device, with no visible frame drops during the transition.
- **SC-006**: A POI marker is visible at the confirmed location within 500 ms of the viewer finishing its zoom animation, correctly positioned at ground level at the resolved coordinates.

## Assumptions

- The existing Three.js viewer (SPEC-027/035) already supports camera altitude/zoom control — this feature adds the signal that drives it, not the camera control mechanism itself.
- The geocoding provider (Google Maps Geocoding API, SPEC-038 predecessor) returns a `viewport` bounding box in its response — this already exists in the Google Maps Geocoding API response schema.
- Lucy already has a working location-resolution tool (SPEC-037) and an active-location store. This feature extends the payload of that existing tool rather than replacing it.
- Zoom commands are handled in the same agent tool infrastructure used for location resolution — no new AI provider integration is needed.
- "Zoom in" and "zoom out" are relative commands (one meaningful step per invocation), not absolute altitude commands; the viewer defines what one step means.
- Mobile support is in scope since the viewer already targets responsive layouts.
- The POI marker is rendered using the viewer's 3D overlay capability (the same WebGL scene the map uses), so it stays anchored to the correct geographic coordinates as the user pans, tilts, or rotates the view. The Google Maps WebGL Overlay View API is the identified mechanism for this on the frontend — see planning research.
