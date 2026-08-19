# Feature Specification: Flumeria Studio Workspace Shell

**Feature Branch**: `024-flumeria-studio-shell`

**Created**: 2026-08-16

**Status**: Draft

**Input**: User description: "Now we will rename ask lucy chat to be Flumeria \"Studio\", redesign the authenticated Flumeria application shell around a minimal, immersive, AI-native workspace. The 2D/3D viewer must become the visual center of gravity. Avoid conventional permanent toolbars, menus, sidebars, navigation panels, settings panels. Instead, introduce compact circular controls that expand contextually: a control starts as a small circular icon, clicking expands it into a fully rounded pill/rectangle, related controls appear inside, clicking again collapses it back, with smooth micro-interactions and no permanent screen-space consumption. The controls should support viewer tools, switching between 2D/3D view modes, layers, navigation, selection, analysis tools, and other contextual actions. Do not implement every viewer capability in this feature — establish the reusable control system and application-shell architecture that later features can use. Create reusable components for CircularAction, ExpandableActionGroup, FloatingToolbar, ContextualToolbar, FloatingPanel, and WorkspaceOverlay, compatible with the existing MUI-based application. Acceptance criteria: viewer occupies the majority of the viewport full width/full height; no large permanent desktop-style toolbar; controls transition between circular and expanded states; controls work with keyboard and mouse; animations are smooth and unobtrusive; components are reusable by the viewer and chat features; mobile/tablet behavior is considered. Use the referenced floating-button/toggle-menu interaction patterns and the supplied readdy.ai Studio preview as inspiration. Change the page route and title from 'chat' to 'studio'."

## Clarifications

### Session 2026-08-16

- Q: Since this feature explicitly doesn't build real spatial/GIS/BIM content, what should occupy the full-viewport surface? → A: A neutral placeholder viewport (e.g., a soft alternating gradient), reserved for future spatial content. The existing AI particle-sphere visualization relocates into its own separate floating rounded-square card positioned over the surface — distinct from the chat conversation panel — matching the referenced readdy.ai Studio design.
- Q: Should the existing floating chat/assistant panel be rebuilt on the new circular-control primitives now, or left as-is alongside the new viewer-tool controls? → A: Rebuild the chat panel now on the new primitives, so the whole Studio workspace uses one consistent control language from day one.
- Q: Should not-yet-functional tool categories (Layers, Navigation, Selection, Analysis) appear as visible placeholders or stay hidden until each capability ships? → A: Show them now as visible, clearly-labeled "coming soon" placeholders that open and close like any other control but state the capability isn't available yet.
- Q: What is the exact page title and route path for the renamed workspace? → A: Page title "Flumeria Studio"; route path `/studio`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Arriving in the immersive Studio workspace (Priority: P1)

A signed-in user opens what used to be "Chat" and instead lands in "Flumeria Studio": a full-viewport visual workspace with no permanent toolbar, sidebar, or menu bar in sight — just the workspace surface and a handful of small circular controls floating over it.

**Why this priority**: This is the foundational shell change every other requirement depends on. Without the full-viewport surface and the absence of permanent chrome, the contextual-control pattern has nothing to be contextual against.

**Independent Test**: Sign in, land on the workspace, and confirm the main surface fills the entire viewport edge-to-edge, the page title and address reflect "Studio," and no fixed-position toolbar/sidebar/menu bar consumes a persistent strip of the screen.

**Acceptance Scenarios**:

1. **Given** a signed-in user navigates to the workspace, **When** the page finishes loading, **Then** the workspace surface fills 100% of the viewport width and height, the browser tab/page title reads "Flumeria Studio," and the URL path is `/studio`.
2. **Given** the workspace is loaded, **When** the user looks at the screen, **Then** no permanent toolbar, menu bar, sidebar, navigation panel, or settings panel occupies a fixed portion of the screen at all times — only small circular controls are visible, positioned over the surface.
3. **Given** a user has an existing bookmark or shared link to the previous "chat" URL, **When** they open it, **Then** they land in the renamed Studio workspace rather than seeing a broken link or an unrenamed page.

---

### User Story 2 - Reaching tools through a contextual circular control (Priority: P1)

A user wants to switch view modes, or peek at layers, navigation, selection, or analysis options, without a permanent toolbar. They tap a small circular control; it smoothly expands into a rounded container showing the related actions; they pick one or tap the control again to collapse it back down.

**Why this priority**: This is the core interaction pattern the whole feature exists to establish. Every later feature that adds a workspace tool will rely on this expand/collapse mechanism already working correctly.

**Independent Test**: From a freshly loaded workspace, activate each circular control in turn, confirm it expands into a pill/rectangle revealing its related actions, confirm selecting an action or reactivating the control collapses it back to its circular state, and confirm the transitions are animated rather than instant.

**Acceptance Scenarios**:

1. **Given** the workspace is loaded, **When** the user activates a circular control, **Then** it expands into a fully rounded pill or rectangle that reveals the actions or options belonging to that control, with a smooth (non-instant) transition.
2. **Given** a control is expanded, **When** the user activates it again (or performs an equivalent dismiss action), **Then** it collapses back to its original small circular state with a smooth transition.
3. **Given** one control is already expanded, **When** the user activates a different circular control, **Then** the first control collapses and only the newly activated one is expanded, so at most one expanded control occupies the workspace at a time.
4. **Given** the workspace is loaded, **When** the user inspects the available circular controls, **Then** they find entry points for: switching between 2D and 3D view modes, layers, navigation, selection, analysis tools, the AI chat assistant, and account/session access.
5. **Given** a control represents a capability not yet built (layers, navigation, selection, or analysis), **When** the user expands it, **Then** it opens normally and clearly indicates the capability is "coming soon" rather than behaving as if broken or missing.
6. **Given** the user switches the 2D/3D view-mode control, **When** a different mode is selected, **Then** the workspace visibly reflects which mode is currently active.

---

### User Story 3 - Talking to Lucy without permanent chrome (Priority: P2)

A user wants to chat with the AI assistant the same way they always have, but now reaches it through the same small-circular-control pattern as every other workspace tool, rather than a separate, differently-styled toggle.

**Why this priority**: Chat is the platform's core function and must keep working through the transition; folding it into the same control language (rather than leaving its old bespoke toggle in place) is what makes the redesigned shell feel coherent rather than half-migrated.

**Independent Test**: From the workspace, activate the chat control, confirm it expands into the familiar conversation panel, send a message and confirm a response streams in as before, then collapse it and confirm the conversation panel disappears while the workspace remains fully usable underneath.

**Acceptance Scenarios**:

1. **Given** the workspace is loaded, **When** the user activates the chat circular control, **Then** it expands into a floating panel containing the conversation surface, using the same expand/collapse mechanics as the other contextual controls.
2. **Given** the chat panel is open, **When** the user sends a message, **Then** the AI response streams into the panel exactly as it did before this redesign, with no loss of existing chat functionality (sending messages, switching between past conversations, seeing streamed responses).
3. **Given** the chat panel is open, **When** the user collapses it, **Then** it closes back to its circular control and the underlying workspace surface remains fully visible and interactive.

---

### User Story 4 - Operating the workspace without a mouse (Priority: P2)

A keyboard-only user needs to reach, expand, use, and dismiss every circular control using only the keyboard, with no functionality available exclusively to mouse/touch users.

**Why this priority**: The whole shell is being rebuilt around a novel interaction pattern; if that pattern isn't keyboard- and screen-reader-accessible from the start, every feature built on top of it inherits the same gap.

**Independent Test**: Using only a keyboard, tab through the workspace, confirm each circular control receives visible focus, confirm it can be expanded and collapsed with the keyboard, confirm its expanded contents are reachable by further keyboard navigation, and confirm a screen reader announces each control's expanded/collapsed state.

**Acceptance Scenarios**:

1. **Given** the workspace is loaded, **When** the user presses Tab repeatedly, **Then** focus visibly moves between the circular controls in a predictable order.
2. **Given** a circular control has keyboard focus, **When** the user presses Enter or Space, **Then** the control expands, and its revealed actions become reachable by continued keyboard navigation.
3. **Given** an expanded control has keyboard focus inside it, **When** the user presses Escape, **Then** the control collapses and focus returns to the circular control that triggered it.
4. **Given** a screen reader is active, **When** a control expands or collapses, **Then** the change in state is announced so a non-visual user knows whether the control is open.

---

### User Story 5 - Consistent experience across devices (Priority: P3)

A user on a phone or tablet gets a workspace that still fills the screen and still exposes every contextual control, sized and positioned so nothing overlaps or falls off-screen.

**Why this priority**: Reach matters, but the desktop interaction pattern is the primary design target being established; adapting it to smaller viewports can follow once the core pattern is validated, without blocking the shell's initial delivery.

**Independent Test**: Load the workspace at mobile, tablet, and desktop viewport widths and confirm the viewer still fills the screen and every circular control remains reachable, legible, and non-overlapping at each size.

**Acceptance Scenarios**:

1. **Given** a mobile-width viewport, **When** the workspace loads, **Then** the viewer still fills the full viewport and circular controls reposition/resize to remain tappable without overlapping each other or being clipped off-screen.
2. **Given** a tablet-width viewport, **When** a control is expanded, **Then** its expanded content remains fully visible within the viewport rather than extending off-screen.

---

### Edge Cases

- What happens if a user rapidly activates a control multiple times while its expand/collapse animation is still playing? The control must settle into a single consistent state (fully expanded or fully collapsed) rather than getting stuck mid-transition.
- What happens if the user taps/clicks outside an expanded control? It collapses, consistent with the Escape/re-activation dismissal behavior.
- What happens when a user has an operating-system or browser "reduce motion" preference enabled? Expand/collapse still occurs but with minimal or no animated motion.
- What happens if two circular controls would visually overlap at a very narrow viewport width? Their layout must adapt (e.g., stack, reposition, or resize) rather than overlap illegibly.
- What happens if the AI Presence Card's particle-sphere scene fails to initialize (e.g., WebGL unavailable)? The circular controls, chat panel, and neutral workspace surface remain fully usable; the card shows a simple static fallback (e.g., Lucy's static portrait) instead of failing silently or breaking the page.
- What happens when a user who was mid-conversation in the old chat experience is the first to load the renamed Studio workspace? Their existing conversations and messages remain intact and reachable through the chat control.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST rename the authenticated workspace's page title to "Flumeria Studio" and its primary route to `/studio`, replacing the previous chat-named title and URL.
- **FR-002**: Existing links or bookmarks pointing at the previous `/chat` URL MUST continue to work by redirecting to `/studio`, without requiring the user to re-authenticate or losing their place in the product.
- **FR-003**: The workspace's main visual surface MUST occupy the full width and full height of the viewport as the persistent visual layer beneath all floating controls.
- **FR-004**: The workspace MUST NOT present a permanent desktop-style toolbar, menu bar, sidebar, navigation panel, or settings panel that occupies a fixed portion of the screen at all times.
- **FR-005**: Every workspace control — view-mode switching, layers, navigation, selection, analysis tools, the chat assistant, and any other contextual action — MUST be reachable only through compact circular controls that expand on demand and collapse when dismissed.
- **FR-006**: Activating a circular control MUST expand it into a fully rounded pill- or rectangle-shaped container revealing the actions or options belonging to that control.
- **FR-007**: An expanded control MUST be collapsible back to its original circular state via the same activation method used to expand it, or an equivalent dismiss action (e.g., Escape, selecting outside its bounds).
- **FR-008**: Expansion and collapse MUST use smooth, animated transitions rather than appearing or disappearing instantly, except where the user has a reduced-motion preference enabled (FR-018).
- **FR-009**: Every circular control and its expanded contents MUST be fully operable using mouse/touch (click/tap) and using keyboard alone (focus, activate, navigate within, and dismiss).
- **FR-010**: The workspace MUST provide circular-control entry points for, at minimum: switching between 2D and 3D view modes, layers, navigation, selection, analysis tools, the AI chat assistant, and account/session access.
- **FR-011**: Selecting a 2D or 3D view mode from its control MUST visibly change which mode the workspace surface is currently in.
- **FR-012**: The layers, navigation, selection, and analysis tool controls MUST be visible and operable (expand/collapse normally) even though their underlying functionality is not built by this feature; each MUST clearly communicate that the capability is "coming soon" rather than appearing broken, unresponsive, or indistinguishable from a working control.
- **FR-013**: The AI chat assistant MUST be reachable from the workspace through the same circular-control expand/collapse pattern as every other contextual control, expanding into a floating panel that contains the conversation surface.
- **FR-014**: All existing chat functionality (sending messages, receiving streamed responses, switching between past conversations, viewing message history) MUST continue to work unchanged once relocated behind the chat circular control.
- **FR-015**: When a user activates a circular control while a different one is already expanded, the system MUST collapse the previously expanded control so that at most one expanded control's contents occupy the workspace at a time.
- **FR-016**: The system MUST establish a reusable set of workspace-shell building blocks: a single compact circular control; a group of related actions that expand and collapse together; a floating cluster of controls positioned over the surface independent of what's selected; a toolbar whose available actions change based on what is currently selected or active; a floating panel for richer content such as a conversation; and an overlay layer that hosts and coordinates all floating controls and panels above the main surface.
- **FR-017**: The reusable workspace-shell building blocks MUST be implemented in a way that is compatible with, and does not conflict with, the design system and components already used elsewhere in the authenticated application.
- **FR-018**: The system MUST honor a user's reduced-motion preference by using minimal or instant transitions in place of the default animated expand/collapse motion.
- **FR-019**: The system MUST expose each control's current expand/collapse state to assistive technology so that screen reader users can perceive whether a control is open before interacting with its contents.
- **FR-020**: The workspace's main surface and every circular control MUST remain usable — without overlapping each other or being clipped off-screen — across mobile, tablet, and desktop viewport widths.
- **FR-021**: This feature MUST NOT implement the underlying functional behavior of layers, navigation, selection, or analysis tools (e.g., real layer data, real selection logic, real analysis computation) beyond presenting their entry points and a "coming soon" state; only the 2D/3D view-mode switch and the chat assistant are required to be functionally complete.
- **FR-022**: The workspace's full-viewport visual surface MUST be a neutral placeholder (e.g., a soft alternating gradient) reserved for future spatial/model content; it MUST NOT be the existing AI particle-sphere visualization.
- **FR-023**: The existing AI particle-sphere visualization MUST be relocated into its own separate floating rounded-square card positioned over the workspace surface — distinct from the chat conversation panel and from the circular action controls — consistent with the referenced design direction.
- **FR-024**: The workspace MUST provide a circular-control entry point for account/session access that preserves every destination currently reachable from the existing account menu (Profile, Settings, Documents, Knowledge Bases, Memory Center, Prompts, Agents, Workflows, Admin panel where applicable, Privacy Policy, and Log out) and for toggling light/dark theme — removing the permanent top bar MUST NOT remove access to any of these.

### Key Entities

- **Studio Workspace**: The renamed authenticated main page (formerly "Chat") where the full-viewport surface and all contextual controls live.
- **Workspace Surface**: The full-viewport, persistent visual layer beneath the floating controls; a neutral placeholder (soft alternating gradient) today, reserved for future 2D/3D spatial content.
- **AI Presence Card**: A small, floating rounded-square card positioned over the Workspace Surface that displays the AI particle-sphere visualization, kept visually and functionally distinct from the chat conversation panel.
- **Circular Action**: A single compact, circular control representing one command or one entry point into a related group of actions, with a collapsed and an expanded visual state.
- **Expandable Action Group**: A set of related actions that appear together once a Circular Action expands, and collapse back into that single circular icon together.
- **Floating Toolbar**: A cluster of one or more Circular Actions/Expandable Action Groups positioned over the workspace surface at a fixed screen location, independent of what's currently selected.
- **Contextual Toolbar**: A toolbar whose available actions change based on what is currently selected or active within the workspace (e.g., analysis actions that only appear once something is selected).
- **Floating Panel**: A larger floating container — such as the chat conversation surface — that opens from a Circular Action and can hold richer content than a simple action list.
- **Workspace Overlay**: The coordinating layer that hosts every Floating Toolbar, Contextual Toolbar, and Floating Panel above the Workspace Surface, and ensures only one expanded control is open at a time.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On first load of the Studio workspace, the surface visibly fills the entire viewport with no permanent toolbar, sidebar, or panel occupying a fixed portion of the screen, verified across desktop, tablet, and mobile widths.
- **SC-002**: A user can locate and open any contextual control (view mode, layers, navigation, selection, analysis, chat, account) within two interactions from a freshly loaded workspace, without external instructions.
- **SC-003**: Expand and collapse transitions complete within roughly 300 milliseconds and are perceived as smooth, with no visible stutter, on typical modern desktop and mobile hardware.
- **SC-004**: Every interactive control can be reached and fully operated using only a keyboard, with no action available exclusively to mouse or touch users.
- **SC-005**: 100% of existing bookmarked or shared links to the previous `/chat` URL land the user at `/studio` rather than a broken or unrenamed page.
- **SC-006**: Existing chat functionality (sending a message, receiving a streamed reply, switching conversations) works with zero regressions after being relocated behind the new chat control, verified against its pre-redesign behavior.
- **SC-007**: When a later feature adds a new floating control or panel to the viewer or chat experience, it visibly matches the same circular expand/collapse pattern established here, so users never encounter two competing floating-UI styles within the workspace.
- **SC-008**: In post-launch review, at least 90% of returning users correctly find the AI chat assistant and at least one other contextual control (e.g., view-mode switch) without guidance.

## Assumptions

- "Ask Lucy" remains the underlying AI engine name (per the existing Flumeria/Ask Lucy brand relationship); this feature renames the authenticated workspace page itself to "Studio" within the Flumeria brand, it does not rename the product or the AI assistant's name.
- The floating assistant panel introduced by the prior immersive-workspace redesign is superseded by, and rebuilt on top of, this feature's reusable circular-control and Floating Panel primitives, rather than left running alongside them as a separate pattern.
- The existing AI particle-sphere visualization is preserved as a visual asset but is repositioned into its own dedicated floating rounded-square card over the workspace surface, separate from the chat conversation panel, rather than filling the entire workspace surface.
- Only one expanded control is shown at a time; opening a new one collapses whichever was previously open.
- View-mode (2D/3D) selection and other transient control states are scoped to the current session and do not need to persist across browser restarts.
- This feature covers the authenticated Studio workspace shell only; the public landing experience and sign-in/sign-up flows (already redesigned separately) are unaffected.
- "Analysis tools," "layers," "navigation," and "selection" are established here purely as reachable, clearly-labeled placeholder entry points; the real data and logic behind each is delivered by later, separate features.
- Visual styling direction (spacing, color, iconography) draws on the previously supplied reference design and floating-button interaction examples, refined to fit the existing design system rather than copied pixel-for-pixel.
- The existing account menu (Profile, Settings, cross-app navigation, Log out) and theme toggle — today reachable only via `MinimalTopBar` on this page — are preserved by relocating them behind a new circular control, not dropped; this feature does not change what's reachable, only how it's reached.
