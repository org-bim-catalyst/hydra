# Feature Specification: Viewer Map Capture

**Feature Branch**: `083-viewer-map-capture`

**Created**: 2026-10-19

**Status**: Draft

**Input**: User description: "Is there a way to make Lucy capture the viewer map in satellite mode in memory and save it as image on the server local storage? Association: capture tied to both conversation and user. Cap at 10 MB, and to reduce the size, crop the picture by 14px from each side. It can be shareable within a conversation/knowledge base. Retention: there are two scenarios — first the captured image can be passed to a caller from memory as a stream, so this can be part of a pipeline, or if the user asks it can be captured and stored on localhost until user or another endpoint retrieves it. The image name can be a combination of the chat id and a timestamp concatenated together. Scope update: front-end capture is deferred to a follow-up feature; this specification covers the server-to-server capture service only."

## Context

This feature provides a server-to-server map capture service. A trusted service calls it with a location — a place name/address or coordinates — and optionally a map mode, overlay preference, and view size. No viewer, browser, or frontend session is involved. The server geocodes the location when needed, obtains imagery in the requested mode (satellite base imagery by default), crops 14 px from each edge, enforces the 10 MB cap, and returns the image as a stream in the same request. Nothing is persisted.

Front-end capture from the live viewer, persistence tied to a conversation and user, sharing within conversations or knowledge bases, and download/showcase UI are explicitly deferred to a follow-up specification.

## Clarifications

### Session 2026-10-19

- Q: How does a capture obtain the map extent when no viewer is open? → A: The caller supplies a location (place name/address or coordinates); the server geocodes it if needed and captures around it.
- Q: When overlays and a mode different from the on-screen mode are both requested, how should the capture be produced? → A: Client-side capture is deferred to a follow-up feature; this specification covers server-to-server capture only.
- Q: What does a caller get when it specifies nothing? → A: Cropped satellite base imagery with no labels, markers, or pins; no overlays.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Server-to-server map capture (Priority: P1)

A downstream service (a pipeline, an agent step, another application) calls the capture service with a location and receives a cropped image of that location. No user, viewer, or frontend is involved. The caller may name a map mode, request overlays, and set the view size; otherwise it gets cropped satellite base imagery centred on the location.

**Why this priority**: This is the whole feature: an automated caller has no browser and must be able to obtain map imagery on its own.

**Independent Test**: With no viewer open, call the service with a place name and no other parameters; confirm the response is a cropped, unlabelled satellite base image centred on the geocoded location, and that nothing was persisted. Repeat with coordinates plus a requested mode and confirm the mode is applied.

**Acceptance Scenarios**:

1. **Given** no viewer or frontend session exists, **When** a caller supplies a place name and names no mode, **Then** the service geocodes the place and returns cropped, base-map satellite imagery centred on it, with no labels, markers, or pins.
2. **Given** a caller supplies coordinates, **When** the capture completes, **Then** the image is produced around those coordinates without geocoding.
3. **Given** a caller names a mode, **When** the capture completes, **Then** the delivered image uses that mode instead of the satellite default.
4. **Given** a caller names no view size, **When** the capture completes, **Then** the service uses a default neighbourhood-scale view around the resolved location.
5. **Given** an ephemeral capture has been delivered, **When** the request completes, **Then** no file, record, or other trace of the capture remains on the server.
6. **Given** a location cannot be resolved, **When** a capture is requested, **Then** the caller receives an explicit failure, not an empty or partial image.
7. **Given** two captures requested in a row or concurrently, **When** both complete, **Then** each caller receives its own image with no cross-contamination.
8. **Given** a caller requests Lucy's overlays, **When** the requested overlays cannot be produced for that location or mode, **Then** the caller receives an explicit failure rather than an image with the overlays silently missing.

---

### Edge Cases

- A caller supplies no location: the capture is refused with an explicit error.
- A supplied place name cannot be geocoded, or geocoding returns no match: the caller receives an explicit failure.
- Geocoding returns several matches: the top-ranked match is used.
- The resolved location has no imagery coverage for the requested mode: the caller receives an explicit failure.
- The caller requests a mode the platform does not offer: the caller receives an explicit failure naming the available modes.
- The cropped image still exceeds 10 MB: the capture is refused with an explicit error; nothing partial is delivered.
- The captured area is smaller than 28 px in either dimension (i.e. cropping 14 px per edge would leave nothing): the capture is refused with an explicit error.
- The imagery provider times out or returns an error: the caller receives an explicit failure and no partial image.
- Two captures requested at the same moment: both succeed independently.
- A caller requests client-only overlays (presence markers, pins) on the server path: the caller receives an explicit failure naming what is unavailable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST capture a still image of the requested location in a requested mode (roadmap, satellite, hybrid, or buildings-only). No viewer or frontend session may be required.
- **FR-002**: The system MUST crop 14 px from each of the four edges of every captured image (left, right, top, bottom) before delivery.
- **FR-003**: A captured, cropped image MUST NOT exceed 10 MB. A capture that exceeds the limit after cropping MUST be refused with an explicit error and no partial image delivered.
- **FR-004**: The system MUST deliver the image as a stream in the same request and MUST NOT persist it: no stored file, database record, or other trace may remain.
- **FR-005**: Server-to-server callers MUST authenticate as trusted services; unauthenticated or unauthorised calls MUST be refused.
- **FR-006**: Capture failures MUST be surfaced to the caller through an explicit failure response; no capture may fail silently or return a partial image.
- **FR-007**: When the caller names no mode and no overlays, the system MUST return satellite base imagery with no labels, markers, or pins.
- **FR-008**: Captures MUST default to base-map imagery only. A caller may explicitly request Lucy's overlays; unsupported or unavailable overlays MUST produce an explicit failure rather than being silently omitted.
- **FR-009**: A caller MUST supply a location, given either as coordinates or as a place name/address. A request without a location MUST be refused.
- **FR-010**: When the caller supplies a place name/address, the system MUST geocode it and capture around the resolved coordinates, using the top-ranked match when several are returned.
- **FR-011**: The system MUST NOT depend on any viewer, browser, or user session to produce a capture.
- **FR-012**: When the caller supplies no view size, the system MUST use a default neighbourhood-scale view around the resolved location that shows its surroundings.
- **FR-013**: The system MUST be safe to call concurrently: independent requests MUST NOT share or contaminate each other's image.
- **FR-014**: The service MUST be read-only with respect to platform data: a capture request MUST NOT create or modify conversations, knowledge bases, users, or stored files.

### Key Entities *(include if data involved)*

- **Map Capture**: A still image of a location in a requested mode, with parameters (location, mode, overlay preference, view size, capture time). Ephemeral — delivered as a stream and discarded.
- **Location**: A place name/address to be geocoded, or a pair of coordinates.
- **Service Caller**: A trusted downstream service authenticated to use the capture endpoint. It has no conversation, user, or persisted ownership.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 95% of valid capture requests complete within 10 seconds, including geocoding when required.
- **SC-002**: 100% of delivered images have exactly 14 px removed from each edge compared with the captured area.
- **SC-003**: 100% of images exceeding 10 MB after cropping are refused with zero partial delivery.
- **SC-004**: 100% of captures leave no persisted trace (no file, record, or other server state).
- **SC-005**: 100% of unauthenticated or unauthorised capture requests are refused.
- **SC-006**: 100% of capture failures return an explicit failure; zero silent failures or partial images.
- **SC-007**: 100% of requests naming no mode and no overlays return unlabelled satellite base imagery with no markers or pins.
- **SC-008**: 100% of requests given a place name are centred on the top-ranked geocoded location.
- **SC-009**: 100% of valid capture requests succeed with no viewer or frontend open.

## Assumptions

- This specification covers server-to-server capture only. Client-side capture from the live viewer, persisted captures tied to a conversation and user, sharing in conversations/knowledge bases, and download/showcase UI are deferred to a follow-up specification.
- Server-to-server captures are ephemeral only, so the conversation/user association and chat-id+timestamp naming requirements do not apply here.
- Coordinates take precedence when a caller supplies both coordinates and a place name.
- When geocoding returns several matches, the top-ranked result is used.
- The default view is neighbourhood-scale (approximately 1 km across) unless the caller supplies a view size.
- Server-side overlays are limited to overlays the platform can render without a viewer or chat context. Building footprints are supported; site boundaries require the chat-scoped, AI-assisted boundary resolver and are refused explicitly on this path until a chat-independent lookup exists. Client-only overlays such as presence markers and pins are not available on this path and are refused explicitly when requested.
- Existing geocoding and mapping-provider integrations are reused; how service credentials are issued is an integration concern for planning.
- The service returns PNG; other formats are out of scope unless required by the delivery path.
- Captures are still images only; video or animated captures are out of scope.
- Rate limiting per service credential is a planning concern, not defined here.
