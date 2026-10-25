# Feature Specification: Solar Dome on Its Own Overlay Surface

**Feature Branch**: `087-solar-dome-overlay-canvas`

**Created**: 2026-10-24

**Status**: Draft

**Input**: User description: "Draw the solar sun-path dome in its own transparent canvas layered ABOVE the Google Maps canvas, so the map's 3D building pass can no longer paint over it."

## Context

The sun-path dome is drawn today inside the map's own WebGL pass: the viewer's renderer is constructed on the WebGL context Google Maps hands to `WebGLOverlayView`. That sharing is the problem. Maps draws its 3D buildings **after** the overlay's draw callback returns and does not honour the depth the overlay wrote, so any building standing between the camera and the dome is painted over it — legitimately, by the map's own rules, since Google documents this shared depth buffer as "3D building geometry occludes overlay content", not the reverse.

Three fixes have already shipped against this and all three are confirmed present in production:

1. `depthTest: false` on the dome's shell, tubes and marker (6ea37540).
2. Explicit draw ordering for the dome's groups (eb5c2283, fbaaed80).
3. A depth mask drawn at the near plane, whose vertex shader pins `gl_Position.z` so every later fragment behind the dome should fail its depth test (fbaaed80).

None of them can work, because none of them can reach a pass that runs after ours. **No amount of in-scene ordering or depth manipulation inside the interleaved pass can stop a later pass from painting over it.** The only fix that can work is to stop sharing the pass: draw the dome on a separate, transparent surface composited above the map.

This feature therefore separates the dome from the map's render pass and leaves everything else — buildings, shadows, shadow ground, sun light — exactly where it is. It also deletes the three workarounds above, which existed only to fight the shared pass and would otherwise become misleading ballast.

Two facts shape the specification.

1. **Only the dome moves.** The massing, the shadow-casting light and the shadow ground plane need the map's own depth and lighting environment; they belong in the interleaved pass and are unchanged. The dome is an instrument drawn *over* the analysis, with no physical interaction with the buildings, so it is the one thing that belongs on the overlay.
2. **The overlay must be registered to the map exactly.** A second surface that is off by a pixel, a frame late, or a different size reads worse than an occluded dome: the dome would visibly slide against the buildings it describes. The camera for the overlay must therefore be derived from the same map transform the interleaved pass uses, every frame, and the surface must track the map's size and pixel ratio.

## Amendments to earlier specifications

This specification ships on top of several earlier ones and changes none of their scope. Where a requirement below carries a pointer, read the pointer with it.

| Specification | What this adds to it |
|---|---|
| [specs/050-viewer-extension-framework](../050-viewer-extension-framework/spec.md) | A capability's content may now be drawn on a second viewer-owned surface, not only in the shared scene. The framework's rule that the viewer owns redraw scheduling and that capabilities never touch the renderer (FR-020, FR-023) is unchanged and now applies to both surfaces. |
| [specs/051-viewer-scene-content-api](../specs/051-viewer-scene-content-api/spec.md) | The one published reference point (`SceneAnchor`) and its local ENU frame remain the only coordinate basis for the dome. Only the surface it is drawn on changes. |
| [specs/052-solar-analysis](../052-solar-analysis/spec.md) | The dome's content, its fixed-versus-dated split (FR-021/FR-022) and its disposal scope (FR-040) are unchanged. FR-038 ("declare drawing features, never change the viewer's drawing settings") is extended: the second surface is created and configured by the viewer, never by the capability. |
| [specs/076-building-conflation-height-raster](../076-building-conflation-height-raster/spec.md) | The dome's radius rule (`sunPathDomeRadiusFor`, sized to enclose the building under study) is unchanged; the dome is still rebuilt at the new radius when the site changes. |
| [specs/086-solar-hour-lines](../086-solar-hour-lines/spec.md) | The hour lines move with the rest of the dome onto the overlay, and the theme re-tint mechanism is unchanged. |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The Dome Is Never Hidden Behind a Building (Priority: P1)

A designer opens the solar analysis over a dense site and tilts the camera down so the massing stands between them and the dome. The dome — shell, compass dial, monthly lattice, hour lines, the chosen day's arc, both solstices, hour marks and the sun marker — stays fully visible where it stands over the site, in front of every building, at every camera angle, heading and zoom.

**Why this priority**: This is the entire feature. Every other requirement is a correctness property of this surface.

**Independent Test**: Open the analysis on a site whose surroundings contain tall buildings, tilt and rotate the camera so buildings occupy the screen between the viewer and the dome, and confirm that no part of the dome is painted over by any building at any angle — including the case where a building's roof is in front of the dome's shell, its lattice, its arcs or its marker.

**Acceptance Scenarios**:

1. **Given** a site with tall buildings around the dome, **When** the camera is tilted and rotated until buildings stand between the camera and the dome, **Then** the whole dome remains visible and no part of it is painted over by a building.
2. **Given** any camera heading, tilt or zoom, **When** the dome is drawn, **Then** the dome's shell, dial, mount post, monthly lattice, hour lines, chosen-day arc, solstice arcs, hour marks and sun marker are all visible on the same surface.
3. **Given** the dome is visible in front of a building, **When** the user opens the same view with the analysis closed, **Then** the buildings render normally with no residue of the dome.

---

### User Story 2 - The Dome Stays Registered to the Map (Priority: P2)

A designer pans, zooms, rotates and tilts the map, resizes the window, and toggles the light/dark theme while the analysis is open. The dome stays locked to the ground exactly where it was: rotating the camera turns the dome with the buildings, zooming grows it with the site, resizing the window does not shift or stretch it, and a theme change re-tints its hour lines without moving anything.

**Why this priority**: A mis-registered overlay is worse than an occluded one — a dome that slides against the map is visibly wrong and undermines trust in everything else the analysis reports.

**Independent Test**: Measure a fixed point of the dome (the dial's north label, the sun marker) against a fixed map feature at two camera angles and two window sizes; the screen offset between them must be unchanged.

**Acceptance Scenarios**:

1. **Given** a drawn dome, **When** the camera rotates, tilts, zooms or pans, **Then** the dome stays anchored to the same ground position and the same scale as the map's own content.
2. **Given** a drawn dome, **When** the map container resizes (window resize, browser side panel, layout change), **Then** the dome's surface matches the new size and the dome's position stays correct with no stretch or offset.
3. **Given** a drawn dome, **When** the device's pixel ratio changes (window moved between displays, browser zoom), **Then** the dome stays sharp and registered.
4. **Given** a drawn dome, **When** the app theme changes, **Then** the hour lines are re-tinted to the new theme's colour and nothing else about the dome changes.
5. **Given** the analysis is open, **When** the user navigates away from the workspace and returns, **Then** the dome is drawn again in the same state and place, per the extension's existing "keep workspace state across navigation" rule.

---

### User Story 3 - No Workarounds, No Silent Failures, Nothing Left Behind (Priority: P3)

An engineer reading the solar code finds no near-plane depth mask, no explicit draw-order juggling and no depth-testing opt-outs on the dome's materials — each was a workaround for the shared pass and each is gone now that the dome has its own surface. If the second surface cannot be created, or a frame fails, the failure is reported and the rest of the viewer keeps working. Closing the analysis, or the map itself, leaves no surface, context or geometry behind.

**Why this priority**: Removing the failed workarounds is part of the change — left in place they would read as load-bearing. Failure handling and disposal are required for the feature to be safe to ship, but they do not change what the user sees when it works.

**Independent Test**: Force the second surface to fail to create and confirm a reported failure plus a working map; then close and reopen the analysis repeatedly and confirm no extra surfaces, contexts or geometry accumulate.

**Acceptance Scenarios**:

1. **Given** a working viewer, **When** the dome is drawn, **Then** there is no near-plane depth mask, no capability-specific draw-order override, and no material on the dome that disables depth testing in order to beat the map.
2. **Given** the overlay surface cannot be created, **When** the viewer draws, **Then** the failure is reported to the console through the existing error path and the map, buildings, shadows and the rest of the viewer continue to work.
3. **Given** a frame throws while drawing the dome, **When** the failure is caught, **Then** it is reported through the same path and the map's own content for that frame is unaffected.
4. **Given** the analysis has been opened and closed repeatedly, **When** the user inspects the page, **Then** exactly one overlay surface exists at a time and none after the analysis is closed.
5. **Given** the map layer is being torn down (route change, map recreation), **When** teardown completes, **Then** the overlay surface, its drawing context and the dome's geometry are all released.

### Edge Cases

- **Overlay surface unavailable**: the browser's WebGL context limit is reached or the surface cannot be created. The failure is reported; the map and its buildings keep rendering; the analysis remains usable except for the dome.
- **Context loss and restore**: the GPU resets (driver update, sleep/wake, tab backgrounded). The overlay is released and recreated, and the dome reappears without a page reload.
- **Map recreation**: a theme toggle, a map-style change or returning to the workspace recreates the whole map layer. Exactly one overlay surface is created for the new map and the old one is released — no stacked, stale surfaces.
- **Analysis closed**: the analysis's content is hidden. The overlay surface stays fully transparent rather than showing a dark or stale rectangle.
- **Analysis never opened**: no dome content is drawn and no overlay frame work beyond a clear happens.
- **Nothing changed between frames**: no redraw is requested and no frame is drawn; the overlay follows the same redraw cadence as the map, not a loop of its own.
- **Raster or flat map**: no WebGL overlay pass exists, so no overlay surface exists either. Nothing regresses: the dome was never drawn in that mode.
- **Camera looking straight down or at a very shallow angle**: the dome stays registered and its content stays visible.
- **A building directly between the camera and the dome, closer than the near plane**: a building can visually "cut into" the dome's shell; because the surface is above the map, the shell still reads over it — the dome's own depth sorting (shell behind arcs, arcs behind marker) remains correct within itself.
- **Two capabilities drawing at once**: the site-boundary highlight and other interleaved content are unaffected by the dome's surface.
- **Device with reduced quality**: the reduced-quality path still applies to both surfaces; no second pixel-ratio policy is introduced.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The sun-path dome MUST be drawn on its own transparent surface composited **above** the map's rendering surface, so that no part of the map's 3D building pass can paint over any part of the dome.
- **FR-002**: The dome content MUST move as a whole to the new surface: enclosing shell, compass dial, mount post, monthly lattice, hour lines, chosen-day arc, both seasonal extremes, hour marks and the current sun position marker.
- **FR-003**: The building massing, the shadow-casting sun light and the shadow ground plane MUST remain in the map's interleaved pass, unchanged in appearance and behaviour.
- **FR-004**: The overlay surface MUST be positioned and sized so that it registers exactly with the map's own drawing surface: same origin, same extent, same pixel ratio; it MUST NOT intercept pointer input; and it MUST stay in sync when the map's container is resized or its pixel ratio changes. Map gestures (pan, zoom, rotate, tilt, click) MUST reach the map unimpeded.
- **FR-005**: Each frame, the overlay's camera MUST be derived from the same map transform the interleaved pass uses, so the dome lines up with the map's own geometry at every camera position, heading, tilt and zoom. The derivation MUST be a pure, independently testable function.
- **FR-006**: The overlay surface and its renderer MUST be created, configured and owned by the viewer's map bridge, next to the existing renderer. No capability may create, configure or hold a reference to it (specs/052 FR-038: renderer-global settings stay in the viewer).
- **FR-007**: The overlay MUST draw on the same redraw cadence as the map's existing pass — one draw per requested frame, driven by the existing redraw scheduler and the existing per-frame subscription for capability animation. It MUST NOT introduce a second animation-frame loop of its own.
- **FR-008**: No silent failures. A failure to create the overlay surface, and any failure while drawing an overlay frame, MUST be reported through the existing error path (console error, in the same way the interleaved pass reports). A failure on either surface MUST NOT prevent the other surface from drawing that frame.
- **FR-009**: The overlay renderer, its drawing context, its surface element and the dome's geometry MUST be released when the map layer is disposed, when the drawing context is lost, or when the analysis is torn down. Repeated open/close cycles and map recreations MUST leave no accumulated surfaces, contexts or geometry.
- **FR-010**: The dome MUST keep its present position, size and orientation: the same published reference point (`SceneAnchor`), the same local ENU frame, and the same radius rule (`sunPathDomeRadiusFor`) as today. Moving to the new surface MUST NOT change where the dome is drawn in the world.
- **FR-011**: The near-plane depth mask (`buildDomeOccluder`) MUST be removed, along with every reference to it.
- **FR-012**: The capability-specific draw-order overrides on the dome's groups MUST be removed, and the depth-testing opt-outs on the dome's shell, tubes and marker MUST be removed. Inside its own surface the dome MUST depth-sort normally against itself — the shell behind the arcs, the arcs behind the current-position marker — and transparent ordering MUST follow ordinary transparent-object behaviour.
- **FR-013**: A theme change MUST still re-tint the hour lines in place, through the existing mechanism, without rebuilding the dome's geometry.
- **FR-014**: Activating, deactivating and re-activating the analysis MUST show and hide the dome as today; a hidden dome MUST leave the surface fully transparent.
- **FR-015**: The "keep workspace state across navigation" rule MUST hold: leaving the workspace and returning MUST NOT reset the dome, the camera, the panels or the analysis state.
- **FR-016**: This feature MUST NOT change the solar-position calculation, the shadow behaviour, the analysis panels, the figures, the day-length chart, the site-boundary highlight or any backend behaviour.
- **FR-017**: A pure helper that derives the overlay camera's projection and pose from the map transform MUST exist and be covered by a unit test.
- **FR-018**: The ownership split MUST be covered by a test: the dome's objects belong to the overlay surface and are absent from the interleaved scene; the massing, shadow ground and sun light belong to the interleaved scene and are absent from the overlay.
- **FR-019**: Disposal MUST be covered by a test: on drawing-context loss and on layer removal, the overlay renderer is disposed and the surface is removed, and no second surface remains.
- **FR-020**: Content MUST reach the overlay through the viewer's existing extension seam only: a capability places its objects on a viewer-owned overlay and is given no renderer, camera or scene reference (specs/052 FR-037/FR-038). Capabilities that draw in the interleaved pass MUST be unaffected by the overlay's existence, and the overlay's lifetime MUST be independent of any single capability.

### Key Entities

- **Overlay surface**: the transparent, non-interactive drawing surface the viewer owns, registered over the map surface. It exists only while the map's WebGL overlay is available.
- **Dome content**: the whole sun-path instrument — fixed furniture (shell, dial, mount post, monthly lattice, hour lines) and dated content (chosen-day arc, seasonal extremes, hour marks, current sun marker). Its lifetime rules (specs/052 FR-021/FR-022) are unchanged.
- **Interleaved pass**: the existing map pass shared with Maps' own rendering, where the massing, sun light and shadow ground plane remain.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With the camera placed so that buildings stand between the viewer and the dome, **100%** of the dome's drawn silhouette is visible — at every heading (0–360°) and tilt (0–67.5°) tested. Today the same test fails wherever a building covers the dome.
- **SC-002**: The dome's ground registration error against the map is **imperceptible**: a chosen fixed dome feature and a chosen fixed map feature hold the same screen offset before and after a camera rotation, a zoom, and a container resize.
- **SC-003**: Opening the analysis and rotating the camera continuously shows **no new frame-rate regression** compared with the same gesture before the change (measured on the same hardware; the dome's own rendering budget is unchanged).
- **SC-004**: Three previously shipped workarounds are gone: no near-plane depth mask, no dome draw-order overrides, no depth-testing opt-outs on dome material. A code search for each returns nothing in the solar feature.
- **SC-005**: Ten consecutive open/close cycles of the analysis, plus three map recreations (theme toggles), leave **exactly zero** overlay surfaces and zero drawing contexts after the analysis is closed, and **exactly one** while it is open.
- **SC-006**: A forced failure to create the overlay surface yields **one** reported error and a map that still pans, zooms, rotates, renders buildings and shadows, and shows its site boundary.

## Assumptions

- The map bridge continues to be the owner of all rendering surfaces, as it is today; the second surface is its second renderer, not a new abstraction layer.
- The overlay is available whenever the map's WebGL overlay is available (a vector map). On a raster/flat map neither exists, and the dome was never drawn there — no regression is introduced.
- The dome has no lighting requirement of its own: its materials are already unlit (basic/standard without scene light dependence for the shell and arcs), so the overlay needs no light rig, no environment map and no tone-mapping change. It is cleared to full transparency each frame rather than being tone-mapped against the map.
- Shadows cast by the dome onto the map are not a requirement and are not produced today; moving the dome to its own surface does not remove a visible effect.
- The reduced-quality path (a lower effective resolution on small viewports) continues to be applied by the viewer to both surfaces through the same policy; this feature does not introduce a second policy.
- The map's own capture/snapshot pipeline renders imagery server-side and is unaffected by a new live DOM surface.
- Existing test harness conventions apply: pure helpers are unit-tested in node/jsdom, and renderer/DOM lifecycle is tested with a stubbed map bridge, as `GoogleMapsGisLayer.test.ts` already does.
