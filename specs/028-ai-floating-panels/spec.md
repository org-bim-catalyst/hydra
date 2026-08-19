# Feature Specification: AI-to-UI Floating Panel Framework

**Feature Branch**: `028-ai-floating-panels`

**Created**: 2026-08-17

**Status**: Draft

**Input**: User description: "Implement an extensible AI-to-UI floating panel framework for the Flumeria Three.js viewer. The objective is to allow Ask Lucy and other AI agents to dynamically present visual responses as interactive floating panels over the viewer. Panels must support floating positioning, dragging, resizing where appropriate, minimizing, closing, z-index/layer management, transparency, opacity control, context association, and communication with the viewer/model. The architecture should allow the AI agent to request a panel without hardcoding every possible panel into the main application, and be reusable by future AI tools and agents."

## Clarifications

### Session 2026-08-17

- Q: When the spec says new panel types can be added "without hardcoding every possible panel," what does that extensibility mechanism actually mean? → A: A registry of developer-built renderers. Each panel type (chart, site-analysis, etc.) has a purpose-built renderer registered under a type key; the AI selects an existing type by key and supplies data matching that type's expected shape. Introducing a genuinely new visual type still requires one isolated developer addition (a new renderer + registration), but the core panel management and viewer code never changes.
- Q: When an AI panel request doesn't specify a position, where should the new panel appear? → A: Cascade from a fixed corner. Each new panel opens offset from the previous one (stepping down-and-right, wrapping once it nears the opposite edge), so panels opened in sequence remain individually visible and grabbable rather than stacking exactly on top of each other.
- Q: Should there be a cap on concurrently open panels, and what happens past it? → A: Cap at a fixed maximum; when a new request would exceed it, the least-recently-focused open panel is automatically closed to make room, so the user is never blocked and the AI-driven flow is never interrupted.
- Q: What range should the user-configurable panel opacity setting cover? → A: A bounded range with a readability floor (e.g., 40%-100%), so users can make panels more or less transparent but never below a level that keeps panel content legible.
- Q: Are floating panels private to the user who triggered them, or shared/visible to everyone viewing the same model session? → A: Private per user, consistent with the platform's existing per-user isolation (conversations, knowledge bases, memory). No cross-user sync is required; panel state lives entirely within the triggering user's own session.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - AI Presents a Visual Response as a Floating Panel (Priority: P1)

A user is exploring a design or site inside the immersive viewer and asks Ask Lucy a question that calls for a visual answer (e.g., "show me the site analysis" or "chart the daily sun exposure"). Instead of only replying in the chat panel, Ask Lucy presents the response as a floating panel that appears over the viewer, so the user can see the visualization directly in the spatial context it relates to.

**Why this priority**: This is the core value of the feature — without the ability for an AI response to become a visible floating panel, none of the interaction, layout, or extensibility capabilities matter. It is also the smallest slice that proves the end-to-end path (AI response → panel → viewer overlay) works.

**Independent Test**: Can be fully tested by triggering an AI response that includes a visual result (e.g., a chart or a site-analysis summary) and confirming a floating panel appears above the viewer displaying that content, while the viewer remains visible and interactive behind it.

**Acceptance Scenarios**:

1. **Given** a user is viewing a model in the immersive viewer, **When** Ask Lucy produces a response that includes visual data (e.g., a chart, table, or analysis summary), **Then** a floating panel appears over the viewer displaying that content, without the user needing to leave the viewer or navigate to another page. If the response did not specify a position, the panel appears at the next position in the cascade sequence so it does not sit exactly on top of any already-open panel.
2. **Given** an AI response requests a panel type that is new to the system (e.g., a panel type introduced after this framework was built), **When** the request is handled, **Then** the panel renders correctly as long as the new type has been registered with the framework, with no changes required to the core viewer or panel-management logic.
3. **Given** an AI response requests a panel type that is unknown or unregistered, **When** the request is handled, **Then** the user sees a visible, understandable fallback or error indication rather than the request silently failing or nothing happening.

---

### User Story 2 - User Manages Panel Layout (Priority: P2)

A user has one or more AI-generated panels open over the viewer and wants to arrange their workspace: moving a panel out of the way, resizing a panel to see more detail, minimizing a panel they want to keep but not look at right now, closing a panel they're done with, and bringing a specific panel to the front when several overlap.

**Why this priority**: Once panels can appear (P1), users immediately need to control their layout so panels don't obstruct the viewer or each other. This is essential for the feature to be usable in practice, but depends on P1 existing first.

**Independent Test**: Can be fully tested by opening two or more panels and independently dragging, resizing (where supported), minimizing, closing, and focusing each one, confirming the viewer and other panels are unaffected by actions taken on one panel.

**Acceptance Scenarios**:

1. **Given** a floating panel is open, **When** the user drags its title area, **Then** the panel moves smoothly to follow the pointer and stays where released.
2. **Given** a floating panel of a resizable type is open, **When** the user drags its edge or corner, **Then** the panel's size changes accordingly and its content adjusts to fit.
3. **Given** a floating panel of a fixed-size type is open, **When** the user attempts to resize it, **Then** the panel communicates that it is not resizable (e.g., no resize handles are shown) rather than behaving unpredictably.
4. **Given** a floating panel is open, **When** the user selects minimize, **Then** the panel collapses to a compact representation and can be restored to its previous size and position on request.
5. **Given** a floating panel is open, **When** the user selects close, **Then** the panel is removed from the viewer and its resources are released.
6. **Given** multiple panels are open and overlapping, **When** the user interacts with (clicks or drags) a panel that is behind another, **Then** that panel is brought to the front and visually indicated as focused.

---

### User Story 3 - User Controls Panel Transparency (Priority: P3)

A user finds that floating panels, even semi-transparent by default, still obscure too much of the underlying viewer, or conversely wants panels more visible. From Settings, the user adjusts panel opacity and sees the change reflected on their panels.

**Why this priority**: This refines the experience established by P1 and P2 and directly satisfies an explicit requirement (opacity control from Settings), but the feature is still meaningfully usable without it, since panels ship with a sensible default transparency.

**Independent Test**: Can be fully tested by opening the Settings area, changing the panel opacity/transparency control, and confirming open (and subsequently opened) floating panels reflect the new opacity level.

**Acceptance Scenarios**:

1. **Given** one or more floating panels are open, **When** the user changes the panel opacity setting in Settings, **Then** the open panels update to reflect the new opacity.
2. **Given** the user has previously set a panel opacity preference, **When** the user returns in a later session and opens a new panel, **Then** the panel appears using the previously saved opacity preference.
3. **Given** no opacity preference has been set, **When** a panel is first opened, **Then** it renders semi-transparent by default rather than fully opaque or invisible.

---

### User Story 4 - Panel Reacts to and Informs the Viewer (Priority: P3)

A user is looking at a floating panel showing, for example, site-analysis results tied to a specific area of the model. Interacting with the panel (e.g., selecting a data point) highlights or focuses the related element in the viewer, and changes in the viewer (e.g., the user selecting a different object or location) can update what a related open panel shows.

**Why this priority**: Two-way communication between panels and the viewer is what elevates the panels from static overlays to context-aware tools, fulfilling the "context association" and "communication with the viewer/model" requirements. It builds on P1–P2 and is not required for the most basic version of the feature to deliver value.

**Independent Test**: Can be fully tested by opening a panel that carries a reference to a specific viewer element/location, triggering an action in the panel, and confirming the viewer responds accordingly (e.g., highlight, focus, or camera movement), and vice versa where applicable.

**Acceptance Scenarios**:

1. **Given** a floating panel is associated with a specific object or location in the viewer, **When** the user interacts with the panel in a way that references that association, **Then** the viewer visibly reflects that association (e.g., highlighting or focusing the related element).
2. **Given** a floating panel is open and associated with viewer context, **When** the relevant state in the viewer changes, **Then** the panel's content can be informed of that change (e.g., an "outdated" indicator or a refreshed value), rather than silently going stale with no indication.

---

### Edge Cases

- What happens when an AI panel request includes malformed, incomplete, or unexpected data for its declared panel type? The panel should show a visible error/fallback state rather than rendering blank or crashing the viewer.
- What happens when an AI response requests a panel type the system has never registered? The user should see a clear, visible fallback rather than nothing happening.
- How does the system behave when so many panels are open that they cover most or all of the viewer? The user must still have a way to see and interact with the viewer (e.g., via minimize-all, close-all, or moving panels); additionally, once the maximum concurrent panel count (FR-022) is reached, the least-recently-focused panel auto-closes to make room for new AI-requested panels.
- What happens when a user tries to resize a panel below a usable minimum size, or drags a panel entirely outside the visible viewer area?
- What happens when two or more AI panel requests arrive at nearly the same time? Each should result in its own independently manageable panel, without one overwriting or corrupting another.
- What happens when a user closes a panel while the AI is still actively producing or streaming content intended for it? The system should stop directing updates to the closed panel without error.
- What happens when the browser/viewport is resized while panels are open? Panels should remain accessible and usable (e.g., repositioned to stay within the visible area) rather than becoming permanently off-screen or unreachable.
- What happens when a panel's associated viewer context (an object or location it refers to) no longer exists (e.g., the object was removed from the scene)? The panel should indicate the association is no longer valid rather than referencing a broken or missing element silently.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow an AI agent to request that a floating panel be displayed, by specifying at minimum a registered panel type (by key), a title, and the data to display, without the core panel management or viewer application containing type-specific logic for every panel type.
- **FR-002**: System MUST render AI-requested panels as floating overlays positioned above the 3D viewer surface.
- **FR-003**: System MUST keep the underlying viewer visible and interactive (navigation, selection, and other existing viewer interactions continue to work) while one or more panels are open.
- **FR-004**: Users MUST be able to reposition any open panel by dragging it to a new location within the viewer surface.
- **FR-005**: System MUST support resizing for panel types where resizing is appropriate (e.g., panels showing tables, charts, or dashboards), and MUST clearly indicate when a panel type does not support resizing.
- **FR-006**: Users MUST be able to minimize an open panel to a compact state and restore it to its prior size and position.
- **FR-007**: Users MUST be able to close an open panel, which removes it from view and releases any resources it was using.
- **FR-008**: System MUST support multiple panels being open at the same time without one panel's state or content affecting another's.
- **FR-009**: System MUST manage panel stacking order so that the panel most recently interacted with is visually brought to the front and indicated as focused.
- **FR-010**: Panels MUST render with a semi-transparent (non-opaque) background by default.
- **FR-011**: Users MUST be able to configure panel opacity from Settings within a bounded range that always keeps panel content legible (opacity cannot be lowered to the point of practical invisibility), and this preference MUST apply to currently open panels and to panels opened afterward.
- **FR-012**: System MUST persist the user's panel opacity preference so it is remembered across sessions.
- **FR-013**: System MUST allow a panel to be associated with specific contextual information from the viewer (e.g., a related object, location, or state), so panel content and viewer state can reference one another.
- **FR-014**: System MUST support communication between an open panel and the viewer in both directions: actions taken in a panel can affect the viewer (e.g., highlighting or focusing a related element), and relevant changes in the viewer can inform an associated panel's content.
- **FR-015**: System MUST allow new categories of AI-generated visual content (e.g., dashboards, charts, tables, design recommendations, site/GIS/environmental analysis, urban design metrics, diagrams, parameter/control panels, alternative design proposals) to be introduced by registering a new panel type (a type key plus its renderer) with the framework, without requiring changes to the core panel management or viewer logic.
- **FR-016**: System MUST handle a request for an unknown, unregistered, or invalid panel type by surfacing a visible error or fallback state to the user rather than failing silently.
- **FR-017**: System MUST handle malformed or incomplete panel data by surfacing a visible error/fallback state within that panel rather than leaving it blank, broken, or crashing the viewer.
- **FR-018**: System MUST keep panels within the usable viewer viewport (or otherwise recoverable, e.g., via a reset/reposition affordance) so a panel can never become permanently inaccessible.
- **FR-019**: System MUST release/clean up panel resources when a panel is closed so that opening and closing panels repeatedly does not degrade viewer or application performance over time.
- **FR-020**: System MUST provide a way for the user to regain full, unobstructed view of the viewer even when multiple panels are open (e.g., minimizing or closing panels in bulk).
- **FR-021**: When a panel request does not specify a position, system MUST place the new panel using a cascading offset from a fixed starting corner of the viewer (each successive panel opens offset from the last, wrapping back toward the starting corner before reaching the opposite edge), so panels opened in sequence remain individually visible and reachable.
- **FR-022**: System MUST enforce a fixed maximum number of concurrently open panels; when a new panel request would exceed that maximum, the system MUST automatically close the least-recently-focused open panel to make room rather than blocking or rejecting the new request.
- **FR-023**: Panels MUST be private to the user whose AI interaction triggered them; no other user MUST be able to see, open, or affect a panel they did not trigger.

### Key Entities

- **Floating Panel**: A single AI-requested UI overlay instance rendered above the viewer. Carries a type, title, content/data, position, size, opacity, minimized/closed state, and stacking (focus) order.
- **Panel Type Definition**: The registered pairing of a type key (e.g., "site-analysis," "chart," "parameter-controls") with a purpose-built renderer that defines what data the type expects and how that data is presented. New AI response categories are introduced by registering a new panel type definition rather than modifying core framework logic; the AI selects among already-registered types when requesting a panel.
- **Panel Request**: The message an AI agent sends to ask for a panel to be created, referencing a panel type, a title, the data to render, and optionally initial placement and opacity.
- **Viewer Context Association**: The link between a panel and a specific element, location, or state within the viewer, enabling the panel and the viewer to reference and react to one another.
- **User Panel Preferences**: User-level settings related to panels, including the panel opacity preference, persisted across sessions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When an AI response includes visual content, a corresponding floating panel appears over the viewer within 2 seconds of the response being ready.
- **SC-002**: Users can reposition any open panel to a new location in one continuous drag motion, with the panel visibly following the pointer throughout.
- **SC-003**: Users can minimize, restore, and close any open panel in a single action each, with no more than one attempt required.
- **SC-004**: At least 5 floating panels can be open at the same time without the viewer becoming unresponsive or unusable.
- **SC-005**: A user-adjusted opacity setting in Settings is reflected on all open panels immediately, without requiring a page reload.
- **SC-006**: A new AI-generated visual response type can be added to the framework by defining its data shape and presentation, without modifying the core panel management or viewer code — verified by successfully introducing one new panel type end-to-end without core framework changes.
- **SC-007**: Panel requests referencing an unregistered type or containing malformed data produce a visible fallback/error state in 100% of cases, with zero silent failures.
- **SC-008**: Users retain full use of existing viewer capabilities (pan, zoom, rotate, select) while panels are open, up to and including the maximum supported panel count, with no loss of functionality.

## Assumptions

- The AI-side decision of *what* to show (which panel type, what data) is produced by Ask Lucy's existing chat/agent capabilities; this feature covers the framework that turns such a request into a rendered, interactive floating panel and does not define new AI reasoning behavior.
- The floating panel framework applies to the immersive Three.js viewer/studio surface introduced in prior specs (Flumeria studio/viewer), not to the general chat message list.
- Panel opacity is a single global user preference applied to all panels, rather than a per-panel or per-panel-type setting, unless a future need for finer-grained control is identified.
- Panel layout state (which panels are open, their positions/sizes) is scoped to the current viewer session; only the opacity preference is expected to persist across sessions.
- Resizing is only meaningful for panel types whose content benefits from more space (e.g., tables, charts, dashboards); simple fixed-content panel types (e.g., short recommendations or single controls) may be fixed-size by design.
- "Communication with the viewer/model" refers to contextual, event-based interaction (e.g., highlight on selection, update on relevant state change), not continuous real-time data streaming.
- The existing Settings area of the application will be extended with a panel opacity control rather than introducing a separate settings surface.
- The framework is designed to be reusable by any current or future AI agent/tool in the platform (per the platform's multi-agent architecture), not exclusively by a single agent.
- The viewer is a single-user surface today (no existing collaborative/shared viewer session); panels are therefore private to the triggering user and require no cross-user real-time sync.
