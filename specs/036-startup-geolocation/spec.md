# Feature Specification: Startup Geolocation and Live Location Context

**Feature Branch**: `036-startup-geolocation`

**Created**: 2026-08-23

**Status**: Draft

**Input**: User description: "When the app is loaded it detects the current location and load it into the viewer, when the agent is confident about the location the user asked, it will use the same approach to replace the start up location with the location the user confirmed. You may need also to update the temperature widget and the location name."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - App Startup Detects and Loads Current Location (Priority: P1)

When a user opens the Ask Lucy app, the application automatically requests the user's device location. If permission is granted, the viewer, temperature widget, and location name display all update to reflect the user's current location — without any user input or navigation.

**Why this priority**: This is the foundational experience. A user arriving at the platform should immediately see their context in the viewer and relevant environmental data (weather) for their location. It sets the "active location" baseline that all subsequent interactions build on.

**Independent Test**: Can be fully tested by opening the app in a browser that has location services available, granting permission when prompted, and verifying that the viewer centers on the device location, the temperature widget shows local weather, and the location name display shows the detected place name — all within a few seconds.

**Acceptance Scenarios**:

1. **Given** the app loads for the first time in a session, **When** the user grants the location permission prompt, **Then** the viewer centers and frames on the user's current geographic position, the temperature widget updates to show current conditions at that location, and the location name display shows a human-readable name for the detected position.
2. **Given** the app is loading, **When** the location detection is in progress, **Then** a visible loading indicator is shown in the location name display and/or temperature widget area, and the viewer holds a neutral state until the location is available.
3. **Given** the app loads, **When** the device location is obtained successfully, **Then** the entire transition (permission prompt → detection → viewer/widget update) completes and the location is visible within the app.

---

### User Story 2 - Location Permission Denied or Unavailable (Priority: P1)

If the user denies the location permission prompt, or if the device cannot determine a location within a reasonable time, the app loads gracefully into a neutral state. The viewer, temperature widget, and location name display show appropriate empty or placeholder states. No crash, no blocked UI, and no confusing error screen.

**Why this priority**: Permission denial is a common and expected user choice. The app must remain fully functional without location data — location context is a convenience enhancement, not a requirement for using Ask Lucy.

**Independent Test**: Can be tested by denying the browser's location permission prompt (or blocking it in browser settings) and verifying that the app loads completely with neutral placeholder states, that no error blocks the UI, and that the user can still interact with Lucy normally.

**Acceptance Scenarios**:

1. **Given** the app loads and requests location permission, **When** the user denies the permission, **Then** the viewer, temperature widget, and location name display each show an appropriate empty/placeholder state, and the user can proceed to use the app without interruption.
2. **Given** the app requests device location, **When** location detection times out without a result, **Then** the app falls back to the same neutral state as a denied-permission scenario and the user is not blocked.
3. **Given** the location detection fails for any reason, **When** the app displays the neutral state, **Then** no error dialog blocks interaction and no crash occurs.

---

### User Story 3 - Agent-Confirmed Location Replaces Active Location (Priority: P1)

When Lucy's agentic system (spec 035) resolves a location that the user confirms, the app uses the same location-loading mechanism as startup to replace the active location. The viewer reframes, the temperature widget updates, and the location name display updates — producing the exact same end-state as if that location had been detected at startup.

**Why this priority**: The startup geolocation and agent-confirmed location replacement must be behaviorally identical from the user's perspective. The same active location concept is the shared contract between these two entry points and all location-aware UI components.

**Independent Test**: Can be tested by asking Lucy to find a named location (per spec 035), confirming it, and verifying that the viewer, temperature widget, and location name display all update in the same way they would have if that location had been detected at startup.

**Acceptance Scenarios**:

1. **Given** a startup location is already loaded in the viewer and widgets, **When** Lucy's agent confirms a different location, **Then** the viewer reframes to the new location, the temperature widget updates to that location's current conditions, and the location name display updates to the new name.
2. **Given** the app started in a neutral state (permission denied), **When** the agent confirms a location, **Then** the viewer loads the confirmed location, the temperature widget shows data for that location, and the location name display shows the confirmed name — as if it had been the startup location.
3. **Given** an agent-confirmed location is active, **When** another agent confirmation occurs for a different location, **Then** the viewer, temperature widget, and location name display all update to reflect the latest confirmed location.

---

### User Story 4 - Location Name and Temperature Stay in Sync with Active Location (Priority: P2)

The temperature widget and location name display are always in sync with the current active location, regardless of how that location was set (startup detection or agent confirmation). Whenever the active location changes, both widgets update automatically without requiring a page refresh.

**Why this priority**: Inconsistency between the viewer and the widgets (viewer showing location A while temperature shows location B) would undermine trust and create confusion, especially for site-analysis workflows.

**Independent Test**: Can be tested by loading any location via either entry point, verifying widget sync, then loading a second location, and verifying that both widgets update together with the viewer.

**Acceptance Scenarios**:

1. **Given** any location is active, **When** the viewer shows that location, **Then** the temperature widget shows weather for the same location and the location name display shows the same place name.
2. **Given** the active location changes, **When** the change occurs, **Then** the temperature widget and location name display update without requiring any user interaction beyond the location change itself.

---

### Edge Cases

- What if the device reports location with very low accuracy (e.g., country-level only)? Should the viewer still load?
- What happens if the weather/temperature data fetch fails after the location is detected?
- What if the reverse geocoding step (coordinates → readable name) fails — should the coordinates be shown as a fallback name?
- If a chat-confirmed location arrives while startup geolocation is still loading, the startup load is cancelled and the chat-confirmed location takes effect immediately. Startup detection cannot displace a chat-confirmed location.
- What if the app is running in a context where the geolocation API is unavailable (e.g., insecure HTTP context, browser extension, embedded iframe)?
- What if the detected device location is in the middle of an ocean — should the viewer still center there?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: On every app load, the system MUST request the user's current geographic position from the device.
- **FR-002**: If the position is obtained successfully, the system MUST immediately establish it as the active location and load it into the viewer, temperature widget, and location name display without any additional user action.
- **FR-003**: The system MUST display a visible loading indicator in the location-aware UI areas while location detection is in progress.
- **FR-004**: If location permission is denied or detection fails for any reason, the system MUST load the app into a neutral state — viewer, temperature widget, and location name display each show an appropriate placeholder — with no crash and no blocked user interaction.
- **FR-005**: Location detection MUST time out after **15 seconds** if no position is returned; on timeout the app MUST fall back to the neutral state described in FR-004.
- **FR-006**: When Lucy's agent confirms a location (per spec 035), the system MUST use the same active-location mechanism as startup detection to update the viewer, temperature widget, and location name display.
- **FR-007**: The temperature widget MUST show weather conditions for the active location. Weather data is fetched once when the active location is established and again each time the active location changes. No time-based background refresh is performed while the same location remains active.
- **FR-008**: The location name display MUST always show a human-readable name for the active location. When coordinates are the source (startup detection), the human-readable name is obtained from the `locationName` field returned by the weather data lookup for those coordinates. No separate reverse-geocoding call is made at startup — the weather response provides the name. If the weather lookup fails or returns no location name, the system MUST display the coordinates as a fallback (`${latitude}, ${longitude}`) rather than leaving the display blank.
- **FR-009**: Whenever the active location changes — from any source — the viewer, temperature widget, and location name display MUST update together, producing a consistent and synchronized state.
- **FR-010**: The location-loading mechanism MUST be shared between the startup geolocation path and the agent-confirmation path; two separate implementations of the same behavior are not permitted.
- **FR-011**: Device-detected coordinates MUST NOT be stored on the Ask Lucy backend as a result of startup detection. Transmitting coordinates transiently as required lookup parameters — for example, as query parameters in a weather data request — is permitted provided the backend does not persist them. Coordinates MUST NOT be sent to the backend for passive background storage or tracking. Coordinates MAY be shared with the backend as part of an explicit, user-initiated analysis workflow that requires location context.
- **FR-013**: At startup, the system MUST request the device's highest-accuracy position first. If a high-accuracy fix is not returned within a short inner window, the system MUST automatically retry using low-accuracy mode rather than waiting the full 15-second timeout. A low-accuracy fix (city-level) is acceptable for loading the viewer and widgets; the fallback MUST be transparent to the user.
- **FR-012**: Location sources have a defined priority order. A chat-confirmed (agent) location is higher priority than a startup-detected location. If a chat-confirmed location arrives while startup geolocation is still loading, the startup load MUST be cancelled and the chat-confirmed location MUST take immediate effect. A completed startup-detected location MUST NOT displace a location that was already set by chat confirmation.

### Key Entities

- **DeviceLocation**: The raw geographic position from the device's location service. Contains latitude, longitude, and optionally an accuracy radius. Serves as the input to the active-location loading process during startup.
- **ActiveLocation**: The single source of truth for the currently loaded location across the entire app for a given session. Populated either by startup geolocation detection or agent-confirmed resolution. Read by the viewer, temperature widget, and location name display. Replaced atomically when a new location is confirmed.
- **TemperatureWidget**: Existing UI component that shows current weather conditions. In this feature it is wired to the active location so its data always reflects the current site.
- **LocationNameDisplay**: Existing UI component that shows a readable name for the current location. In this feature it is wired to the active location and updated via reverse geocoding when coordinates are the source.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When a user opens the app and grants location permission, the viewer centers on their current location and the temperature widget and location name display both reflect that location — all within 5 seconds of permission grant.
- **SC-002**: When the active location changes (via startup detection or agent confirmation), the temperature widget and location name display update within the same interaction without requiring a page refresh or manual action from the user.
- **SC-003**: If location permission is denied or detection does not produce a result within 15 seconds, the app loads to a fully usable state with neutral UI — the user is never blocked, no crash occurs, and no error dialog prevents interaction.
- **SC-004**: The active-location loading mechanism is used identically for both startup detection and agent-confirmed replacement, producing the same viewer, widget, and display outcome in both cases.
- **SC-005**: When coordinates are the source of the active location (startup), a readable place name is shown in the location name display — raw coordinates are never the permanent display value unless reverse geocoding is unavailable.

## Clarifications

### Session 2026-08-23

- Q: What is the device geolocation detection timeout before falling back to neutral state? → A: 15 seconds (consistent with the geocoding search timeout in spec 035).
- Q: Are device-detected coordinates stored on the Ask Lucy backend? → A: No — the backend does not persist device coordinates as a result of startup detection. Coordinates are transmitted transiently as query parameters in the weather lookup (a backend proxy call); the backend does not store them. Coordinates are never sent to the backend for passive background tracking or storage. See FR-011 and plan.md Complexity Tracking for the full rationale.
- Q: How is a simultaneous location change handled when startup detection is still in progress? → A: A chat-confirmed (agent) location always overrides a startup load in progress and takes immediate effect. Startup geolocation cannot displace a location that was set via chat confirmation.
- Q: Which geolocation accuracy mode should the system use at startup? → A: Request high accuracy first with a short inner timeout; if high accuracy is not available within that window, automatically fall back to low accuracy so the startup experience remains responsive.
- Q: Should the temperature widget refresh weather data on a time interval while the same location is active? → A: No — weather data is fetched once when the active location is set and refreshes only when the active location changes. No time-based background refresh.

## Assumptions

- The temperature widget and location name display already exist as components in the app UI and currently show static or disconnected data; this feature wires them to the active location.
- The existing 3D viewer can accept location updates at any time during a session (not only on first initialization) and will reframe to the new position.
- The device geolocation API is available in the target browser environments; environments that do not support it (e.g., very old browsers) are treated the same as a permission-denied scenario.
- The readable place name for a startup-detected location is sourced from the `locationName` field of the weather API response for those coordinates; no separate reverse-geocoding call is made from the browser. This aligns with the plan's implementation (`activeLocationStore.setLocationName()` called in the weather-success callback in `LocationWeatherWidget`). If the weather API returns no name, raw coordinates are displayed as the fallback per FR-008.
- Weather/temperature data for the active location is fetched directly from the browser using a suitable weather data source; the spec does not define that source — it is an existing or to-be-selected infrastructure dependency whose API is accessible client-side.
- Raw device coordinates are never stored on the backend as a result of startup detection; they remain browser-local state for the session.
- If weather data is unavailable for a location, the temperature widget shows an appropriate empty or retry state rather than crashing.
- The active location is session-scoped; it is not persisted across sessions or shared between users.
- Location accuracy: the system first attempts a high-accuracy fix; if unavailable within a short inner window, it falls back to a low-accuracy fix automatically. A low-accuracy fix (city-level) is sufficient to load the viewer and fetch weather; sub-meter precision is not required but is used when readily available.
- The geolocation permission prompt is shown by the browser natively; the app cannot pre-approve or bypass it.
