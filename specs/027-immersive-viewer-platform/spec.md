# Feature Specification: Immersive Viewer Platform for AI-Assisted Urban Design

**Feature Branch**: `027-immersive-viewer-platform`

**Created**: 2026-08-17

**Status**: Draft

**Input**: User description: "Transform the main Flumeria workspace into an immersive Three.js-based 2D/3D visualization environment for AI-assisted urban design. Display a location + weather widget derived from the user's current location, render that location in the viewer via a GIS/map layer, add toolbar controls to toggle isometric vs. plan camera view and to start/stop scene rotation, and architect the viewer as an extensible, layered rendering platform (conceptually similar to the Autodesk Platform Services Viewer) that separates the viewer engine, camera/navigation, GIS layers, model layers, selection/highlighting, overlays, and a programmatic command/event API that later Ask Lucy AI-agent features can call — without building the full AI-agent integration itself."

## Clarifications

### Session 2026-08-17

- Q: When the workspace loads, what should the viewer show by default — the existing audio-reactive abstract sphere, or the user's current-location map/GIS content? → A: The current-location map/GIS view becomes the default content once the user's location is resolved. The existing sphere is preserved as the loading/placeholder state shown before location resolves, and remains available afterward as a separate, user-selectable content mode (not deleted or replaced outright).
- Q: What should happen if the user denies, or does not have, browser geolocation permission, for the weather widget and the map current-location layer? → A: Graceful hidden fallback — the weather widget and the map current-location layer simply do not appear (optionally with a subtle "enable location" affordance); the rest of the workspace, including the viewer itself, remains fully usable, consistent with the existing "never block the workspace" pattern.
- Q: How should the new Google Maps and weather API calls be architected, given the constitution's rule that secrets never live in client bundles? → A: Hybrid — the map layer renders client-side using a domain-restricted public key (the Maps WebGL Overlay View's standard, intended usage model), while weather lookups are proxied through the backend, which holds that provider's key server-side and can rate-limit/log the call.
- Q: Should the user's resolved current location be persisted anywhere beyond the current browser session? → A: No — location and weather stay in session/client-side state only; nothing is written to the database or the user's profile.
- Q: What performance target should the viewer meet once the map/GIS layer is the active content, versus the existing sphere? → A: The same ~60fps target applies to the map/GIS content mode as to the sphere mode, with the same graceful degradation on lower-end devices — one consistent performance bar across all viewer content modes.
- Q: The existing decorative sphere is not currently a full-viewport background — it was deliberately relocated (SPEC-024) into a small, independent corner presence card (`AiPresenceCard`) distinct from the full-viewport surface this feature replaces. Given that, what should the new viewer's placeholder content (shown before location resolves, or when unavailable) be? → A: The corner presence card and its sphere stay exactly as they are today, entirely out of scope for this feature. The new viewer's placeholder is a simple, static, non-sphere background — there is no full-viewport "sphere mode" in the viewer, and FR-004/FR-008/US2-AC3 as originally drafted no longer apply in their original form (updated below).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Arrive in an immersive, extensible viewer workspace (Priority: P1)

A user opens the main Flumeria workspace and is greeted by an interactive 2D/3D viewer that fills the majority of the screen and serves as the primary workspace surface, rather than a secondary decoration behind the chat panel. The viewer is built so that different kinds of content — maps, 3D models, 2D drawings, analysis visualizations — can be shown in it side by side or in sequence, without the workspace being redesigned again each time a new content type is introduced.

**Why this priority**: This is the foundational architectural and visual change every other story depends on. Without an extensible viewer occupying the primary workspace, none of the location/weather, camera-control, or future AI-driven visualization capabilities have anywhere to live.

**Independent Test**: Load the main workspace and confirm the viewer occupies the majority of the viewport, renders without errors, and can host at least two distinct kinds of content (e.g., a static placeholder and a map view) without requiring a different page or layout.

**Acceptance Scenarios**:

1. **Given** a user navigates to the main Flumeria workspace, **When** the page finishes loading, **Then** the viewer occupies the majority of the application viewport and is the primary visual element on the page.
2. **Given** the viewer is displaying its placeholder content, **When** the system has a second kind of content ready to show (e.g., the current-location map), **Then** the viewer can present the new content without the surrounding page layout, toolbar, or assistant panel needing to change.
3. **Given** the workspace has just loaded and the user's location has not yet resolved, **When** the viewer initializes, **Then** it shows a simple, non-blocking static placeholder background rather than a blank or frozen screen — independent of, and not to be confused with, the existing decorative sphere presence card (`AiPresenceCard`), which continues to render separately and is unaffected by this feature.
4. **Given** the existing decorative-sphere presence card and its capabilities (continuous idle rotation, manual orbit/zoom/pan, voice-reactive deformation, reduced-motion handling, low-end device fallback), **When** the new full-viewport viewer platform is introduced alongside it, **Then** the presence card and all of its existing capabilities continue to work exactly as before, unaffected by the new viewer.

---

### User Story 2 - See my current location represented in the viewer (Priority: P1)

A user wants the viewer to ground itself in their real-world context. Once their location is known, the viewer displays a map/GIS view of that location as the primary content, giving the workspace an immediate sense of "where am I working."

**Why this priority**: Grounding the viewer in the user's real location is the central new capability requested for this feature and is what the location + weather widget and camera controls are built around. It is high-value and independently demonstrable once Story 1's viewer foundation exists.

**Independent Test**: Grant location permission, load the workspace, and confirm the viewer transitions from its placeholder background to a map view centered on the resolved location within a reasonable time, without further user action.

**Acceptance Scenarios**:

1. **Given** the user has granted location access, **When** their location is resolved, **Then** the viewer displays a map/GIS view centered on that location, replacing the placeholder background as the active content.
2. **Given** the map view of the user's location is displayed, **When** the user interacts with the camera controls (orbit, zoom, pan, view-mode toggle), **Then** the map content responds the same way any other viewer content would, staying visually coherent with the rest of the platform.
3. **Given** location access has not been granted or fails to resolve, **When** the workspace loads, **Then** the viewer remains on the placeholder background and no map content is requested or shown.

---

### User Story 3 - Control camera perspective and motion (Priority: P1)

A user working in the viewer wants to switch between an angled, three-dimensional isometric view and a flat, top-down plan view depending on what they're doing, and wants to be able to stop the scene from continuously rotating when they need a stable view to inspect content.

**Why this priority**: Camera/navigation control is called out explicitly as required toolbar functionality and is essential to making the viewer usable as a real workspace tool rather than a passive animation — it directly supports Stories 1 and 2 regardless of what content is loaded.

**Independent Test**: With the viewer showing any content, use the toolbar to switch between isometric and plan view, and separately toggle rotation on/off, confirming each control produces an immediate, visible, and reversible change.

**Acceptance Scenarios**:

1. **Given** the viewer toolbar is visible, **When** the user selects the view-mode control, **Then** the viewer camera toggles between an isometric (angled 3D) perspective and a plan (top-down, flat) perspective.
2. **Given** the viewer is in plan view, **When** the user toggles the view-mode control again, **Then** the camera returns to isometric view, and the currently displayed real content (map or other layer — the placeholder is unaffected, per FR-004/FR-017) remains visible and correctly oriented in both modes.
3. **Given** the scene is auto-rotating, **When** the user selects the rotation control, **Then** rotation stops immediately and the scene holds its current orientation until the user manually navigates or re-enables rotation.
4. **Given** rotation is stopped, **When** the user selects the rotation control again, **Then** automatic rotation resumes from a smooth, natural starting point rather than jumping abruptly.
5. **Given** the user has a reduced-motion accessibility preference enabled, **When** the workspace loads, **Then** automatic rotation starts in the stopped state by default, consistent with existing motion-reduction behavior.

---

### User Story 4 - See local weather at a glance (Priority: P2)

A user wants a quick, glanceable readout of their current location's name, temperature, and general weather condition, displayed as a compact widget over the viewer, without having to leave the workspace or ask the assistant.

**Why this priority**: The weather widget is explicitly requested and adds real-world context to the workspace, but it is a self-contained, additive UI element that depends on location resolution (Story 2) rather than being foundational itself.

**Independent Test**: With location access granted, load the workspace and confirm the widget shows a location name, a current temperature, and an icon representing the current weather condition, matching the reference layout style.

**Acceptance Scenarios**:

1. **Given** the user's location has been resolved, **When** current weather data for that location is available, **Then** a widget appears over the viewer showing the location's name, the current temperature, and an icon representing the current condition (e.g., clear, cloudy, rainy, snowy, windy).
2. **Given** the widget is displayed, **When** the underlying weather condition category changes (e.g., clear to rainy) on a subsequent refresh, **Then** the icon and temperature update to reflect the new reading without requiring a page reload.
3. **Given** location access is denied or unavailable, **When** the workspace loads, **Then** the weather widget does not appear, and no error is shown to the user.
4. **Given** the weather data source is temporarily unavailable, **When** the widget would normally display, **Then** the widget either shows a clearly stale/last-known reading with an indication of staleness, or does not appear — but never shows broken or blank content.

---

### User Story 5 - Select and highlight content in the viewer (Priority: P2)

A user (or, in the future, the AI assistant on the user's behalf) wants to point at something in the viewer — a site boundary, a model element, a region of the map — and have it become visibly selected and highlighted, distinct from everything else in the scene.

**Why this priority**: Selection and highlighting are core interaction primitives every future content type (BIM models, urban design proposals, analysis overlays) will depend on, and are explicitly required, but they can be validated against the content already available from Stories 1–2 without waiting for those future content types to exist.

**Independent Test**: With any content loaded in the viewer, programmatically or interactively select an addressable element (e.g., the current-location marker) and confirm it is visually distinguished from unselected content, and can be deselected.

**Acceptance Scenarios**:

1. **Given** the viewer has at least one selectable element loaded, **When** that element is selected, **Then** it is visually highlighted in a way that is clearly distinguishable from unselected content.
2. **Given** an element is currently selected and highlighted, **When** the user selects a different element or clears the selection, **Then** the previous highlight is removed and, if applicable, the new element is highlighted instead.
3. **Given** nothing is selected, **When** the viewer is queried for the current selection, **Then** it reports an empty selection rather than an error or stale reference.

---

### User Story 6 - Expose viewer capabilities for future AI-driven control (Priority: P3)

A platform engineer building a future Ask Lucy AI-agent feature needs a stable, documented set of viewer commands and events (add a layer, highlight a site, zoom to a location, display a model, create an analysis overlay) to call into, without needing to understand or modify the viewer's internal rendering code.

**Why this priority**: This establishes the contract future features will build on, but per explicit scope direction it does not include building the AI-agent integration itself, making it the lowest-priority, foundation-laying story in this feature.

**Independent Test**: Without any AI agent involved, invoke each documented viewer command (e.g., add a layer, zoom to a location, highlight an element) directly and confirm the viewer responds correctly and emits a corresponding event, demonstrating the contract works end-to-end even though nothing yet calls it from an AI agent.

**Acceptance Scenarios**:

1. **Given** the viewer is running, **When** a documented command to add a layer, zoom/navigate to a location, select or highlight an element, or display a piece of content is issued, **Then** the viewer performs the requested action and confirms completion.
2. **Given** a command is issued with invalid or unavailable parameters (e.g., an unknown location, a nonexistent element), **When** the viewer processes it, **Then** it reports a clear failure outcome rather than failing silently or crashing.
3. **Given** a viewer state change occurs (selection changes, view mode changes, content loads, rotation starts/stops), **When** that change happens, **Then** a corresponding event is emitted that an external caller (a future AI agent, analytics, or other UI) could subscribe to.

---

### Edge Cases

- What happens when the browser/device cannot render the interactive viewer at all (no WebGL support)? The workspace MUST fall back to a static, non-interactive representation and the assistant panel MUST remain fully usable, consistent with existing fallback behavior.
- What happens when the user's browser does not support geolocation, or the request times out? The system MUST treat this the same as a denied permission — no weather widget, no map content, viewer stays on its placeholder background, no error interrupts the user.
- What happens when the map/GIS provider is unreachable, rate-limited, or returns an error after location has been resolved? The viewer MUST remain on (or fall back to) the placeholder background rather than showing a broken or empty map, and MUST NOT block any other part of the workspace.
- What happens when the weather data source is unreachable? The widget MUST NOT display broken, blank, or indefinitely "loading" content — it either shows a clearly marked stale reading or does not appear.
- What happens when the user toggles view mode or rotation while content is still loading? The controls MUST remain responsive and apply the requested state as soon as content becomes available, without erroring.
- What happens when the user has a system/browser "reduce motion" preference enabled? Automatic rotation MUST default to off, and any camera transitions (view-mode toggle, zoom-to-location) MUST be minimized or instant rather than animated.
- What happens on low-end hardware where full interactive rendering is too slow? The viewer MUST degrade gracefully (reduced detail, paused rotation) rather than freezing or degrading the responsiveness of the assistant panel or toolbar, consistent with existing performance-degradation behavior.
- What happens when a future content type is not yet implemented but a viewer command references it? The command MUST fail with a clear, caller-visible error rather than being silently ignored.
- What happens when two overlapping pieces of content are both eligible for selection (e.g., a map marker and an overlay above it)? The system MUST resolve selection deterministically (e.g., topmost/foreground content wins) rather than selecting an unpredictable or empty target.

## Requirements *(mandatory)*

### Functional Requirements

**Viewer platform & layering**

- **FR-001**: The main Flumeria workspace MUST render an interactive 2D/3D viewer that occupies the majority of the application viewport and functions as the primary workspace surface.
- **FR-002**: The viewer MUST be structured as a layered platform with clearly separated concerns for: the rendering engine itself, camera/navigation, GIS/map content, 3D/2D model content, overlays, selection, and highlighting — such that a new layer or content type can be added without redesigning existing ones.
- **FR-003**: The viewer MUST support GIS/map content and 3D model/drawing content as distinct, separately manageable layer types that can be composed together in the same scene (e.g., a map layer beneath a model or overlay layer).
- **FR-004**: The viewer's placeholder content (shown before the user's location resolves, or when location is unavailable) MUST be a simple, static, non-interactive background. The existing decorative-sphere presence card (`AiPresenceCard`, SPEC-024) is out of scope for this feature and MUST continue to render independently, unaffected by the new viewer.
- **FR-005**: The viewer MUST provide a non-interactive fallback presentation for browsers/devices that cannot render it, without blocking access to the assistant panel or other workspace functionality.
- **FR-005a**: The viewer MUST sustain approximately 60 frames per second on typical modern desktop/laptop hardware once the map/GIS layer is active, and MUST gracefully reduce visual detail or pause animation on lower-end or mobile devices rather than degrade the responsiveness of the assistant panel or toolbar.

**Location, map, and weather**

- **FR-006**: The system MUST request the user's current location using a standard, permission-based mechanism, and MUST proceed without error if permission is denied, unavailable, or the request fails.
- **FR-007**: When the user's location is resolved, the viewer MUST display a map/GIS view centered on that location as a layer within the viewer, replacing the placeholder background as the active view.
- **FR-008**: When the user's location cannot be resolved (denied, unsupported, or failed), the viewer MUST remain on the placeholder background, and no map content or weather widget MUST be requested or shown.
- **FR-009**: Once the user's location is resolved, the system MUST display a compact widget over the viewer showing the resolved location's name, the current temperature, and an icon representing the current weather condition.
- **FR-010**: The weather widget MUST refresh its data periodically while the workspace remains open, updating the displayed temperature and condition icon as new readings become available.
- **FR-011**: If the weather data source is unavailable, the widget MUST either show a clearly indicated stale/last-known reading or not appear at all — it MUST NOT show blank, broken, or indefinitely loading content.
- **FR-012**: If the user's location becomes unavailable after the map view is active (e.g., permission revoked, browser location services disabled), the viewer MUST revert to the placeholder background and the weather widget MUST stop showing new readings, without a full page reload or loss of assistant panel state.
- **FR-012a**: The map/GIS layer MUST render using a client-side key scoped/restricted to this application's domains; the weather lookup MUST be performed through a backend-mediated request that holds the weather provider's credentials server-side rather than exposing them to the client.
- **FR-012b**: The system MUST NOT persist the user's resolved location or weather snapshot beyond the current browser session — no coordinates, resolved location name, or weather reading are written to the database or associated with the user's stored profile/preferences.

**Camera & navigation**

- **FR-013**: The viewer toolbar MUST provide a control that toggles the camera between an isometric (angled, three-dimensional) view and a plan (top-down, flat) view. This applies to real viewer content (map, model, or overlay layers) when present; while only the placeholder is active, the control remains visible and operable but has no visible effect, since the placeholder has no camera-dependent orientation to change.
- **FR-014**: The viewer toolbar MUST provide a control that starts and stops the scene's automatic rotation independently of the view-mode control.
- **FR-015**: When automatic rotation is stopped, the scene MUST hold its current orientation until the user manually navigates or re-enables rotation.
- **FR-016**: When a user has a reduced-motion accessibility preference enabled, automatic rotation MUST default to stopped on load.
- **FR-017**: Users MUST be able to manually orbit, zoom, and pan the viewer camera using pointer/touch input whenever real content (map, model, or overlay layers) is active. The placeholder background is non-interactive per FR-004 and does not support camera manipulation.

**Selection, highlighting, and overlays**

- **FR-018**: The viewer MUST support selecting an addressable element within any loaded content and visually highlighting it in a way distinguishable from unselected content.
- **FR-019**: The viewer MUST support clearing the current selection, at which point no element remains highlighted.
- **FR-020**: The viewer MUST support displaying overlay content (e.g., analysis visualizations, AI-generated diagrams) above base map or model layers without altering the underlying layer's own data.

**Programmatic API**

- **FR-021**: The viewer MUST expose a documented set of programmatic commands covering, at minimum: adding/removing a layer, navigating/zooming to a location, selecting or highlighting an element, displaying a piece of content, and creating an overlay.
- **FR-022**: Each viewer command MUST report a clear success or failure outcome to its caller; invalid or unavailable parameters MUST produce a caller-visible failure rather than a silent no-op.
- **FR-023**: The viewer MUST emit events for significant state changes, including at minimum: content loaded, selection changed, view mode changed, and rotation started/stopped, so that external callers can observe viewer state without polling.
- **FR-024**: The programmatic command/event API MUST be usable independently of any specific caller — this feature establishes and exercises the contract directly, without requiring an AI agent to be built or connected to use it.

### Key Entities

- **Viewer Session**: The running instance of the viewer within the workspace; tracks whether the placeholder background or the map/GIS content is active, the current camera state, and the current selection.
- **Render Layer**: The base concept of a distinct, independently manageable piece of content in the viewer (e.g., a GIS layer, a model layer, an overlay layer); layers can be added, removed, shown, or hidden without affecting other layers.
- **GIS/Map Layer**: A layer representing geographic/map content, including the current-location view; composable with model and overlay layers.
- **Model/Drawing Layer**: A layer representing 3D model, 2D drawing, or urban-design-proposal content; distinct from but composable with GIS layers (not populated with real content in this feature beyond the extensibility contract).
- **Overlay**: Supplementary visual content (analysis visualizations, AI-generated diagrams) rendered above base layers without modifying them.
- **Camera/View State**: The current camera perspective (isometric or plan) and rotation state (running or stopped) of the viewer.
- **Selection State**: The currently selected element(s) in the viewer, if any, and their highlight status.
- **Viewer Command**: A programmatic instruction issued to the viewer (e.g., add layer, zoom to location, highlight element) with a defined outcome (success/failure).
- **Viewer Event**: A notification emitted by the viewer when its state changes, observable by external callers.
- **User Location**: The resolved current geographic position of the user, used to center the map layer and look up weather; held in client/session state only and never persisted server-side.
- **Weather Snapshot**: The location name, temperature, and condition category used to render the weather widget at a point in time, including how stale it is; session-scoped, not persisted beyond the current browser session.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On load of the main Flumeria workspace, the viewer occupies at least 70% of the visible application viewport across desktop, tablet, and mobile breakpoints.
- **SC-002**: Users with location access granted see the viewer transition from its placeholder background to their current-location map view within 5 seconds of location being resolved, under typical network conditions.
- **SC-003**: Users with location access granted see the weather widget (location name, temperature, and condition icon) appear within 5 seconds of location being resolved, under typical network conditions.
- **SC-004**: 100% of camera-control interactions (isometric/plan toggle, rotation start/stop, manual orbit/zoom/pan) produce a visible response perceptible to the user in under 300 milliseconds.
- **SC-004a**: The viewer sustains smooth motion (no stutter perceptible to users) on typical modern desktop/laptop hardware in its map/GIS content mode, and never causes the assistant panel or toolbar to become sluggish or unresponsive, even on lower-end devices where the scene has scaled itself down.
- **SC-005**: Users with location access denied or unavailable experience no errors, blank widgets, or blocked workspace functionality — the workspace remains fully usable with the placeholder background shown instead.
- **SC-006**: Every documented viewer command (add layer, zoom to location, select/highlight, display content, create overlay) can be executed directly and independently verified to succeed or fail with a clear outcome, without any AI agent involved.
- **SC-007**: The existing decorative-sphere presence card (`AiPresenceCard`) and all of its behaviors (idle rotation, manual navigation, voice-reactive deformation, reduced-motion handling, low-end-device fallback) continue to work exactly as before, unaffected by the introduction of the new viewer, verified with no regressions.
- **SC-008**: Users with a reduced-motion accessibility preference enabled experience no automatic scene rotation by default, verified in 100% of sessions with that preference set.
- **SC-009**: On a device or browser without interactive rendering support, users can still access and use the assistant panel and core workspace navigation via the fallback presentation.

## Assumptions

- The viewer is implemented as a Three.js-based rendering engine, architected conceptually similar to the Autodesk Platform Services Viewer (separated engine, camera/navigation, layers, selection, and API), per explicit product direction rather than an open implementation choice.
- The current-location map/GIS layer in this feature is rendered using the Google Maps WebGL-based overlay approach referenced by the product owner; ESRI and OpenStreetMap are treated as future, extensible layer-type targets and are not implemented in this feature beyond the layer architecture supporting them later.
- The Google Maps client key is a domain-restricted public key suitable for direct browser use (its standard, intended usage model); it is not treated as a server-held secret. The weather provider's credentials, by contrast, are held only server-side and reached via a backend-mediated lookup, consistent with the constitution's rule that secrets never live in client bundles.
- Resolved location and weather data are transient, session-scoped state; this feature does not add location or weather to the platform's long-term memory system, and no new database persistence is introduced for either.
- This feature applies to the main authenticated Flumeria Studio workspace page (the same surface addressed by SPEC-006/SPEC-024). Other specialized pages (Settings, Admin Dashboard, Knowledge Base management, Billing) are out of scope.
- The existing floating assistant/chat widget (SPEC-024/SPEC-026) is unchanged by this feature except for continuing to overlay the viewer as it does today.
- The existing decorative sphere and its presence card (`AiPresenceCard`, SPEC-024) are unaffected by this feature and remain a separate, independent element from the new viewer; the viewer's own placeholder state (before location resolves, or when unavailable) is a simple static background, not a sphere.
- The two new toolbar controls (view-mode toggle, rotation toggle) are added to the workspace's existing toolbar surface rather than introducing a new, separate toolbar.
- "Isometric view" refers to the default angled, three-dimensional camera perspective used for general viewer interaction; "plan view" refers to a top-down, orthographic-style perspective suited to reading map/GIS content. Exact camera angles and transition behavior are implementation details left to the design/planning phase.
- Weather data is sourced from a general-purpose weather data provider selected during implementation; no specific paid subscription is assumed to already exist, and a provider requiring no cost for expected usage volumes is preferred by default.
- The weather widget refreshes on a periodic interval (industry-standard practice, e.g., every 10–30 minutes) rather than continuously polling; exact cadence is an implementation detail.
- Full AI-agent integration (an agent actually issuing viewer commands autonomously) is explicitly out of scope for this feature; only the command/event contract and its direct, non-agent-driven exercise are required.
- BIM models, generic 3D models, 2D drawings, urban design proposals, and AI-generated visualizations are extensibility targets this feature's layer architecture must support conceptually; populating the viewer with real content of those types is left to later features.
- Selection/highlighting in this feature is validated against whatever addressable content already exists in the viewer (e.g., the current-location marker); it does not require BIM or model content to exist first.
