# Feature Specification: Viewer Scene and Content API

**Feature Branch**: `051-viewer-scene-content-api`

**Created**: 2026-09-12

**Status**: Draft

**Input**: User description: "Grow the viewer's published surface from a map-shaped command set into one that can host georeferenced 3D content, so extensions can draw and Lucy can load and control what is shown."

## Context

The viewer's published command surface was built for a map: add a layer, frame a location, change the map style, select a registered element. Everything beyond that — the 3D scene the map is already drawing into, where content is anchored in the world, when a frame is drawn, what an element actually *is* — is private to the one component that owns the map.

Two consequences follow. Extensions cannot draw: specs/050 gives them a lifecycle and somewhere to put a panel, but nothing to render into. And Lucy cannot control content: she can zoom the viewer and open a panel, but she cannot load, replace or clear what it shows. The map arrives because the viewer wires it up internally, not because anything asked for it.

This feature closes both gaps. It is the pivotal specification of the four: the constraints it settles are the ones everything afterwards is built on, and the ones that are expensive to undo.

## Clarifications

### Session 2026-09-12

- Q: Does this feature also move the viewer controls that the chat page currently mounts — the weather widget, marker style selector and rotation toggle? → A: No. They have no dependency on anything here, and this is already the largest specification of the four. They move in a follow-up, which is the right place to answer "where do viewer controls live" now that a viewer-hosted toolbar exists.
- Q: Does this feature perform the one-time renderer settings change, or does the first capability that needs it? → A: This feature performs it. The specification that owns renderer state owns its visual consequences, and the change is not actually specific to any later capability — the existing scene renders with no declared colour handling, which is a correctness gap in its own right. Doing it here means specs/052 inherits a settled visual baseline instead of carrying a scene-wide regression alongside a new feature.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Lucy Loads, Replaces and Clears Viewer Content (Priority: P1)

A user asks Lucy to show them something — a site, a model of a proposed building, a different place. Lucy loads it into the viewer. Asked for something else, she replaces it. Asked to clear it, she does. The viewer stops being a thing that shows one map because that is what it was built to show, and becomes a surface whose content is asked for.

**Why this priority**: This is the headline capability and the one the user asked for directly. It is also the smallest slice that proves content is genuinely driveable rather than internally wired.

**Independent Test**: Can be fully tested by asking Lucy to load content, then to replace it, then to clear it, confirming the viewer reflects each request, and confirming the map still appears on startup without anyone asking for it.

**Acceptance Scenarios**:

1. **Given** the workspace opens, **When** the viewer starts, **Then** the map appears as it does today without requiring a request, so a user never faces an empty surface.
2. **Given** the viewer is showing content, **When** Lucy is asked to show something else, **Then** the new content replaces the old and the old content's resources are released.
3. **Given** the viewer is showing content, **When** Lucy is asked to clear it, **Then** the content is removed and the viewer returns to a defined state rather than a blank one.
4. **Given** content is loading, **When** the user waits, **Then** they can see that something is loading rather than facing an unexplained pause.
5. **Given** content fails to load, **When** the attempt completes, **Then** the user is told what failed and why, and the viewer remains usable.
6. **Given** several pieces of content are loaded, **When** the user or Lucy asks what is shown, **Then** each is identifiable and can be removed independently.

---

### User Story 2 - An Extension Draws Georeferenced Content (Priority: P1)

A capability needs to draw in the world — a shape over a site, a model anchored at a location, an analysis result positioned against real geography. It draws into its own space within the viewer's scene, positions things by real-world coordinates, and what it draws stays correctly placed as the user pans, tilts and zooms.

**Why this priority**: Without this, specs/050's extensions can contribute panels and overlays but cannot render anything spatial, which is the entire point of the viewer. Solar analysis, model inspection and every later spatial capability depend on it.

**Independent Test**: Can be fully tested by an extension drawing a known shape at known coordinates, confirming it appears in the correct real-world position, stays correctly placed through camera movement, and disappears completely when the extension stops.

**Acceptance Scenarios**:

1. **Given** an extension is started, **When** it draws content into the space the viewer gives it, **Then** that content appears in the viewer.
2. **Given** an extension places content by real-world coordinates, **When** the content renders, **Then** it appears at those coordinates and remains correctly positioned as the camera moves.
3. **Given** two extensions both draw, **When** both are active, **Then** neither can disturb the other's content, and neither can alter the viewer's global drawing settings.
4. **Given** an extension stops, **When** it does, **Then** everything it drew is removed and the resources it held are released.
5. **Given** an extension draws far from the current view, **When** the user navigates there, **Then** the content is correctly positioned, not offset or misplaced.

---

### User Story 3 - The User Inspects an Element (Priority: P2)

The user selects something in the viewer — a part of a model — and can see what it is: its identity and the information carried with it. Equally, Lucy can present a list of elements and the user can activate one to select and frame it in the viewer.

**Why this priority**: This is what makes specs/049's actionable content real — a table of elements the user can click through is only possible once elements have stable identities and readable properties. It also turns the viewer from a picture into something queryable, which is the direction the product is heading.

**Independent Test**: Can be fully tested by loading content with element information, selecting an element in the viewer and confirming its identity and properties are available, and separately by activating an element reference in a panel and confirming the viewer selects and frames it.

**Acceptance Scenarios**:

1. **Given** content carrying element information is loaded, **When** the user selects an element, **Then** the system can identify it and read the information carried with it.
2. **Given** an element has been selected, **When** the selection is reported, **Then** it carries an identity stable enough to be referred to later.
3. **Given** a panel presents element references, **When** the user activates one, **Then** the viewer selects that element and brings it into view.
4. **Given** a panel references an element that is no longer present, **When** the user activates it, **Then** they are told it is no longer available rather than nothing happening.
5. **Given** content carries no element information, **When** the user selects it, **Then** they are told there is nothing further to show rather than being shown an empty property list.

---

### User Story 4 - The Viewer Stays Responsive While Several Capabilities Draw (Priority: P2)

Several capabilities are drawing at once — an animated highlight, a model, an analysis result. The viewer stays smooth. Turning a capability off reclaims what it was using rather than leaving it consuming resources invisibly.

**Why this priority**: The platform has already lost frame rate to two drawing surfaces competing, and has already shipped a capability that accumulated drawing resources without releasing them. Without a single owner of drawing and resource accounting, adding capabilities makes the viewer worse in a way that is difficult to attribute.

**Independent Test**: Can be fully tested by running several drawing capabilities together and measuring frame rate against the same scene with one, and by repeatedly starting and stopping a drawing capability while observing that resource use returns to its baseline.

**Acceptance Scenarios**:

1. **Given** several capabilities are drawing, **When** the user navigates the viewer, **Then** interaction remains smooth and no capability's drawing degrades another's.
2. **Given** a capability needs the view redrawn, **When** it asks, **Then** the redraw happens and repeated requests within the same frame result in one redraw, not many.
3. **Given** nothing has changed, **When** the viewer is idle, **Then** it is not redrawing continuously.
4. **Given** a capability is started and stopped repeatedly, **When** this is done many times, **Then** resource use returns to its starting level each time with no accumulation.
5. **Given** content is replaced, **When** the new content loads, **Then** the previous content's drawing resources are released.

---

### User Story 5 - Content That Cannot Be Shown Explains Itself (Priority: P3)

Something cannot be displayed — a format that is not supported, content that will not load, content with no position in the world. The user is told what happened and what, if anything, they can do. The viewer keeps working.

**Why this priority**: Required by the platform's no-silent-failure rule, and it is the requirement that replaces an unexplained empty surface with something a user can act on. It refines the other stories rather than delivering alone.

**Independent Test**: Can be fully tested by attempting to load unsupported, unreachable, corrupt and unpositioned content, and confirming each produces a distinct, understandable explanation while the viewer remains usable.

**Acceptance Scenarios**:

1. **Given** content in an unsupported format, **When** loading is attempted, **Then** the user is told the format is not supported and no partial content is left behind.
2. **Given** content that cannot be reached or is corrupt, **When** loading is attempted, **Then** the user is told loading failed, and the failure is recorded for diagnosis.
3. **Given** content with no position information, **When** loading is attempted, **Then** the user is told it cannot be placed, rather than it being placed somewhere arbitrary.
4. **Given** any content failure, **When** it occurs, **Then** content that was already shown remains shown and the viewer remains usable.

---

### Edge Cases

- What happens when content is requested while other content is still loading? Each request must resolve to a defined outcome, with no partially-loaded content left behind.
- What happens when content is placed very far from where the viewer is currently looking? Positioning must remain accurate; the platform has already experienced content being placed against a stale reference point and landing in the wrong place.
- What happens when two capabilities draw overlapping content at the same position? Both must render according to a defined and stable ordering rather than flickering or arbitrarily winning.
- What happens when a capability asks for a redraw after it has stopped? The request must be ignored safely.
- What happens when a capability's drawing throws an error mid-frame? The failure must be contained and surfaced, and must not stop the rest of the viewer drawing.
- What happens when content is very large — many elements, or large geometry? The viewer must remain usable while it loads, and must communicate if it cannot show it.
- What happens when the user selects where two elements overlap? Selection must resolve to one element deterministically.
- What happens when a capability requests drawing settings that conflict with another's? The conflict must be resolved by a defined rule and reported, not silently decided.
- What happens when the device cannot support what content requires? The user must be told, consistent with the viewer's existing behaviour when 3D is unavailable.

## Requirements *(mandatory)*

### Content

- **FR-001**: The viewer MUST provide commands to load, replace, unload and list content, each identifying the content it acts on.
- **FR-002**: Loaded content MUST have a stable identity by which it can be referred to and removed independently of other content.
- **FR-003**: The map MUST continue to load automatically when the workspace opens, so a user never faces an empty surface, while remaining replaceable through the content commands.
- **FR-004**: Lucy MUST be able to load, replace and clear viewer content through the platform's existing capability mechanism.
- **FR-005**: The user MUST be able to see that content is loading, rather than facing an unexplained pause.
- **FR-006**: Replacing or unloading content MUST release the resources that content held.
- **FR-007**: The system MUST support at least one 3D content format for georeferenced models, and MUST report an unsupported format as such rather than failing obscurely.

### Positioning in the World

- **FR-008**: The viewer MUST own a single reference point from which all drawn content is positioned; no capability may set its own.
- **FR-009**: The viewer MUST publish a conversion between real-world coordinates and the local positioning space capabilities draw in, so that no capability implements its own conversion.
- **FR-010**: The local positioning space's orientation convention MUST be defined once, published, and consistent for every capability.
- **FR-011**: Content MUST be positionable by real-world location, height above ground, orientation and scale.
- **FR-012**: Content MUST remain correctly positioned as the camera moves, and when the viewer's reference point changes.
- **FR-013**: Content supplied without position information MUST be reported as unplaceable rather than placed arbitrarily.

### Drawing

- **FR-014**: Each capability MUST be given its own space to draw into, and MUST NOT be given the ability to alter another capability's content or the viewer's shared drawing state.
- **FR-015**: Content drawn by more than one capability MUST render in a defined and stable order.
- **FR-016**: A capability MUST declare the drawing features it requires rather than changing the viewer's drawing settings itself.
- **FR-017**: Conflicting drawing requirements between capabilities MUST be resolved by a defined rule and reported, never silently decided.
- **FR-018**: The system MUST apply a defined colour and lighting treatment to the viewer's scene, and this change MUST be reviewed for its effect on all existing visual content before it is accepted.
- **FR-019**: A failure while a capability is drawing MUST be contained and surfaced, and MUST NOT prevent the rest of the viewer drawing.

### Redrawing

- **FR-020**: The viewer MUST own when the scene is redrawn. A capability MUST request a redraw rather than driving one.
- **FR-021**: Multiple redraw requests arriving before the next frame MUST result in a single redraw.
- **FR-022**: The viewer MUST NOT redraw continuously when nothing has changed.
- **FR-023**: A capability MUST be able to subscribe to a per-frame notification for content that genuinely changes every frame, and that subscription MUST end when the capability stops.
- **FR-024**: A redraw request from a capability that has stopped MUST be ignored safely.

### Camera

- **FR-025**: The viewer MUST publish the camera's current state, including where it is looking and its orientation and inclination.
- **FR-026**: The viewer MUST emit an event when the camera changes, so capabilities can respond to it.

### Elements

- **FR-027**: The viewer MUST allow an element of loaded content to be selected, and MUST report the selection with an identity stable enough to be referred to later.
- **FR-028**: The system MUST make the information carried with a selected element readable.
- **FR-029**: The viewer MUST be able to select and frame an element identified by its identity, so that a reference presented elsewhere can act on it.
- **FR-030**: Selecting where elements overlap MUST resolve to one element deterministically.
- **FR-031**: Content carrying no element information MUST be reported as such rather than presented as having empty properties.
- **FR-032**: The commands added by this feature that are safe for content to invoke MUST be added to the action allowlist established by specs/049.

### Resources

- **FR-033**: Drawing resources held by a capability MUST be tracked, and MUST be released in full when that capability stops or its content is removed.
- **FR-034**: Repeatedly starting and stopping a capability, or repeatedly replacing content, MUST NOT accumulate resource use.

### Failure

- **FR-035**: Every content failure — unsupported, unreachable, corrupt, unplaceable — MUST produce a distinct, user-understandable explanation and MUST be recorded for diagnosis.
- **FR-036**: A content failure MUST leave already-displayed content displayed and the viewer usable.
- **FR-037**: No failure introduced by this feature may be observable only in logs.

### Compatibility

- **FR-038**: The viewer's existing commands and events MUST keep their current meaning and behaviour; this feature adds to the surface and does not redefine it.
- **FR-039**: Every capability migrated by specs/050 MUST continue to behave identically after this feature.

### Key Entities

- **Viewer Content**: Something loaded into the viewer and shown — the map, a model — with a stable identity, a position in the world, and a lifecycle the viewer manages.
- **Content Source**: Where content comes from, expressed so that the viewer never receives an arbitrary external address.
- **World Placement**: How content sits in the world — location, height, orientation, scale — relative to the viewer's single reference point.
- **Reference Point**: The one real-world position from which all drawn content is positioned. Owned by the viewer.
- **Drawing Space**: The area of the viewer's scene a capability draws into, isolated from every other capability's.
- **Drawing Requirement**: A feature of the viewer's rendering that a capability declares it needs, rather than switching on itself.
- **Frame Subscription**: A capability's registered interest in being notified each frame, ending when it stops.
- **Element**: An addressable part of loaded content, with a stable identity and the information carried with it.
- **Camera State**: Where the viewer is looking and from what orientation and inclination.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Lucy can load, replace and clear viewer content, with a 100% success rate across the supported content forms, and the map still appears on startup without being asked for.
- **SC-002**: Content placed by real-world coordinates appears within an agreed tolerance of its true position, and stays within it through camera movement and after the viewer's reference point changes.
- **SC-003**: No capability can alter another capability's drawn content or the viewer's shared drawing state — verified by inspection and by attempting it, with zero successful attempts.
- **SC-004**: With several capabilities drawing at once, viewer interaction remains smooth, measured against the same scene with a single capability drawing.
- **SC-005**: Starting and stopping a drawing capability 50 times returns resource use to its starting level, with no measurable accumulation.
- **SC-006**: The viewer performs no redraws while idle and nothing has changed.
- **SC-007**: An element selected in the viewer can be identified and its information read, and an element reference presented in a panel selects and frames it, in 100% of cases where the content carries element information.
- **SC-008**: Every content failure produces a distinct user-visible explanation — zero failures are observable only in logs.
- **SC-009**: The colour and lighting change is reviewed against every existing visual capability before acceptance, with a recorded before-and-after comparison for each.
- **SC-010**: Every capability migrated by specs/050 behaves identically after this feature — verified by existing coverage passing without substantive change.

## Scope

### In Scope

- Content commands: load, replace, unload, list; their exposure to Lucy; the loading indication and startup behaviour.
- The single viewer-owned reference point, the published coordinate conversion, its orientation convention, and content placement.
- Isolated drawing spaces for capabilities, declared drawing requirements, and defined draw ordering.
- The one-time colour and lighting treatment of the viewer scene, with a recorded visual review against every existing visual capability.
- Viewer-owned redraw scheduling, request coalescing, and per-frame subscriptions.
- Published camera state and camera change events.
- Element selection, stable element identity, readable element information, and selecting and framing an element by identity.
- Extending specs/049's action allowlist with the commands added here that are safe for content to invoke.
- Resource tracking and release for drawing capabilities and content.

### Out of Scope

- The viewer controls the chat page mounts — the weather widget, marker style selector and rotation toggle. They have no dependency on anything here and move in a follow-up, which is the right place to settle where viewer controls belong now that a viewer-hosted toolbar exists.
- The solar analysis capability (specs/052), including sun position, shadows and building geometry.
- Editing, authoring or transforming content in the viewer. Content is loaded and displayed, not modified.
- Platform-managed element properties held separately from the content itself. Element information comes from the content as supplied.
- Measurement, sectioning, markup and model hierarchy browsing.
- Multiple simultaneous reference points, or content spread so widely that a single reference point is insufficient.
- Any user-facing interface for managing loaded content beyond what Lucy and existing controls provide.

## Assumptions

- One 3D model format is supported initially — a single modern format that carries its own element information — rather than several. Others are added when something needs them, not in anticipation.
- Content is served through the platform's existing file access mechanism with its existing entitlement checks. Neither Lucy nor content may cause the viewer to fetch an arbitrary external address, which would risk leaking user activity and reaching internal systems.
- Element information is read from the content as supplied. A platform-managed store of additional properties is a separate concern and is not introduced here.
- A single reference point is sufficient for the area a user works in at one time. Content spread widely enough to break that assumption is out of scope, and the conversion's accuracy limits are stated rather than assumed unlimited.
- The colour and lighting treatment is applied here rather than by the first capability that needs it, because the specification that owns the viewer's drawing settings owns their visual consequences, and because the existing scene declares no colour handling at all — a correctness gap independent of any later capability.
- Loading indication follows the platform's existing conventions for work in progress; no new mechanism is introduced.
- Content and element state live within the user's own session, consistent with the per-user isolation the platform already applies.
- Capabilities are expected to draw modest amounts of content. Level-of-detail management, streaming and other large-scene techniques are out of scope until something needs them.

## Dependencies

- **specs/050-viewer-extension-framework** — supplies the extension contract and context this feature extends with drawing, content and element capabilities. 050 must land first.
- **specs/049-panel-content-model** — supplies the action allowlist this feature extends, and the content panels that present element information.
- **specs/027-immersive-viewer-platform** — supplies the existing command and event surface this feature adds to without redefining.
- **specs/042-site-boundary-resolution** — supplies an existing drawing capability that exercises the reference point and resource requirements, and that the colour and lighting review must cover.

## Downstream

- **specs/052** — solar analysis; the first capability to declare drawing requirements, subscribe per frame, and place real geometry in the world.
- **Follow-up** — moving the chat page's viewer controls into viewer-hosted controls, settling where viewer controls live.
