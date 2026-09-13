# Feature Specification: Clear-Area Panel Placement & Reopen Tray

**Feature Branch**: `054-panel-placement-reopen`

**Created**: 2026-09-13

**Status**: Draft

**Input**: User description: "Floating panels stay clear of the scene and can be reopened after closing. Today, floating panels (the block-content panels Lucy composes and the live panels extensions like solar analysis contribute — specs/049, specs/050) open by cascading from a fixed corner with no awareness of what else occupies the screen: the page's own account/theme menu and viewer-tool button stack, other already-open panels, and viewer-contributed overlay chrome (a weather widget, a boundary-confidence badge). In a real session this produces panels stacked directly on top of each other and toolbar buttons hidden behind unrelated page chrome — confirmed live: opening two panels from one extension's activation (a time-of-day control and a building-corrections control) placed them almost exactly on top of one another in the same crowded corner already holding a weather widget and a confidence badge. Panels need to open somewhere that is actually clear of the 3D scene's own content and of other UI, spreading out (e.g. side by side) rather than stacking blindly, so the underlying viewer stays usable while multiple panels are open. Separately, closing a panel today discards it entirely — there is no way to get it back short of re-triggering whatever opened it in the first place (asking Lucy again, or reopening an extension). This feature adds a way to reopen a panel that was closed, most naturally a persistent small tray or sidebar listing recently-closed (or currently-minimized) panels the user can click to bring back, without cluttering the workspace when nothing is closed. This applies to every panel in the app, not just one feature's — it is a property of the shared floating-panel system specs/049 and specs/050 built, not something scoped to solar analysis."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Panels open where they can actually be seen and used (Priority: P1)

A user is working in the 3D viewer with the account/theme controls, a viewer toolbar, and a couple of overlay widgets already on screen. Lucy (or a viewer extension) opens one or more floating panels. Each panel appears fully visible, not stacked on top of another open panel, and not covering or covered by the fixed page/viewer chrome that's already on screen.

**Why this priority**: This is the core defect reported live — panels landing on top of each other in a corner already occupied by other UI, making both the panels and the chrome underneath unusable. Without this, the floating-panel system is actively counterproductive in any session with more than one panel or overlay widget open.

**Independent Test**: Open two or more panels in quick succession (from Lucy composing content, or from an extension activation) while page chrome and overlay widgets are present. Each panel is fully visible and independently reachable, and no panel obscures the account/theme menu, the viewer toolbar, or another overlay widget.

**Acceptance Scenarios**:

1. **Given** the viewer is showing its standard page chrome (account/theme controls, viewer toolbar) and one overlay widget, **When** a new panel opens with no explicit position, **Then** the panel appears fully within the viewport, not overlapping the page chrome or the overlay widget.
2. **Given** one panel is already open, **When** a second panel opens without an explicit position, **Then** the second panel appears beside the first (not stacked directly on top of it), and both remain fully visible and individually draggable.
3. **Given** enough panels and overlay chrome are on screen that no fully clear region remains, **When** another panel opens, **Then** the panel is placed to minimize how much of any existing panel or chrome element it covers, rather than defaulting back to a fixed corner regardless of what's already there.
4. **Given** a panel is already open and positioned in a clear area, **When** the browser window is resized, **Then** the panel is kept fully within the new viewport and does not end up newly hidden behind page chrome as a result of the resize.
5. **Given** the currently open panels' combined size is small enough to fit the available clear area without any of them overlapping, **When** placement runs, **Then** the panels are arranged in a non-overlapping grid, maximizing how much of the viewer stays visible.
6. **Given** the currently open panels' combined size is too large to fit without overlap, **When** placement runs, **Then** the panels are arranged in a cascade (offset, Windows-style stacking) instead of forcing an overlapping grid, and a smaller panel in that cascade is always stacked in front of a larger one it overlaps, so the smaller panel is never the one left inaccessible underneath.
7. **Given** the user has manually dragged or resized a panel, **When** a later panel opens or closes, **Then** automatic re-arrangement does not silently undo that manual placement for panels the user has touched.
8. **Given** any number of panels are open, **When** the user explicitly asks the workspace to tidy up, **Then** every open panel is re-arranged using the same grid/cascade logic as automatic placement.
9. **Given** the user is actively dragging a panel, **When** the panel passes over a free (unoccupied) region large enough to hold it, **Then** the system shows a placeholder outline of where the panel would land if released there, distinct from the panel itself, which tracks the pointer as it moves between candidate regions.
10. **Given** the user is dragging a panel over a region that is already occupied or too small to hold it, **When** no valid landing region is under the pointer, **Then** no placeholder is shown, so the user isn't misled into dropping onto a spot that will just overlap something else.

---

### User Story 2 - Reopening a panel the user closed (Priority: P2)

A user closes a panel — either because they were done with it for the moment or by mistake — and later wants it back. Instead of having to re-ask Lucy or re-trigger whatever extension action opened it originally, the user opens a small tray/sidebar of recently-closed panels and clicks the one they want, which reappears with its original content.

**Why this priority**: Closing is currently a one-way, destructive action with no undo. This is a real usability gap, but it's secondary to Story 1 — a panel that reopens into the same broken placement as before only partially helps.

**Independent Test**: Open a panel, close it, then open the reopen tray and select that panel from the list. The panel reappears, fully visible, with the same content/type it had when closed. Close nothing, and confirm the tray affordance is not shown (or is visibly empty/disabled) when there is nothing to reopen.

**Acceptance Scenarios**:

1. **Given** a panel is open, **When** the user closes it, **Then** it appears in the reopen tray instead of disappearing without a trace.
2. **Given** the reopen tray has one or more entries, **When** the user selects an entry, **Then** the corresponding panel reopens with its original title and content/type, placed using the same clear-area placement as any newly-opened panel (Story 1), and the entry is removed from the tray.
3. **Given** no panel has been closed in the current session, **When** the user looks at the workspace, **Then** the reopen tray affordance is not visible, so it never clutters a session where nothing has been closed.
4. **Given** a panel that was minimized (not closed), **When** the user looks at the reopen tray, **Then** that panel does not appear there — minimizing already has its own restore affordance, and is unaffected by this feature.

---

### Edge Cases

- What happens when a panel's associated viewer content (its layer/element) no longer exists by the time the user reopens it from the tray? The panel reopens showing the same stale/invalid association indicator it would already show if that happened while it stayed open (no new behavior needed here).
- What happens when more panels are closed in a session than the tray is designed to hold? The oldest closed-panel entry is dropped silently to make room for the newest; the tray never grows unbounded.
- What happens when every region of the viewport is already occupied by chrome and open panels? The system still places the new panel somewhere on screen (least-overlapping available spot) rather than failing to open it or placing it fully off-screen.
- What happens on a narrow (mobile-width) viewport where there is little or no genuinely clear space? Placement degrades to the least-overlapping option available and panels remain draggable so the user can rearrange them manually; the reopen tray remains reachable rather than being pushed off-screen.
- What happens if the user closes a panel and then closes the *reopened* version of the same panel again? It re-enters the tray as a new (or refreshed) entry; there's no special-casing of "already been reopened once."

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST track the screen regions currently occupied by fixed page chrome (e.g. the account/theme controls, the viewer toolbar) and by viewer-contributed overlay widgets, so panel placement can be made aware of them.
- **FR-002**: When a panel opens without an explicitly requested position, the system MUST place it in a region of the viewport not already occupied by the tracked chrome (FR-001) or by another open panel, when such a region is available.
- **FR-003**: When multiple panels open without explicit positions in close succession, the system MUST spread them apart (e.g. side by side) rather than stacking them at a small fixed offset from one another.
- **FR-004**: When no fully unoccupied region remains for a new panel, the system MUST still open it, choosing the placement that overlaps the least with existing chrome and panels, rather than failing to open or silently placing it off-screen.
- **FR-005**: The system MUST keep every open panel fully within the current viewport bounds after a window/viewport resize, consistent with existing clamp-to-viewport behavior, and MUST re-evaluate clear-area placement so a resize does not newly hide a panel behind chrome.
- **FR-005a**: When the currently open panels can all fit within the available clear area without overlapping each other, the system MUST arrange them in a non-overlapping grid rather than a cascade, to maximize the visible viewer area.
- **FR-005b**: When the currently open panels cannot all fit without overlapping, the system MUST switch to a cascade arrangement (offset, Windows-style stacking) rather than forcing an overlapping grid.
- **FR-005c**: In a cascade arrangement, a smaller panel MUST be stacked in front of (rendered above) any larger panel it overlaps, so a compact panel is never left inaccessible underneath a larger one.
- **FR-005d**: The system MUST NOT override a panel's position or size once the user has manually dragged or resized it, when automatic placement re-runs for a later panel opening, closing, or a viewport resize.
- **FR-005e**: The system MUST offer an explicit user-triggered "arrange" action that re-runs grid/cascade placement (FR-005a/FR-005b) across every currently open panel, including ones the user previously moved manually.
- **FR-005f**: While the user is dragging a panel, the system MUST show a placeholder outline of the free (unoccupied, FR-001/FR-002) region the panel would land in if released at the current pointer position, distinct from the panel being dragged, updating as the pointer moves between candidate regions.
- **FR-005g**: The system MUST NOT show a landing placeholder while the dragged panel is over a region that is already occupied or too small to hold it.
- **FR-006**: Closing a panel MUST move it into a reopen tray (a bounded list of recently-closed panels) instead of permanently discarding its state, retaining enough information (title, panel kind, and its content or live-panel type/data) to reconstruct it.
- **FR-007**: The system MUST provide a persistent UI affordance from which the user can view the reopen tray's contents and select an entry to reopen.
- **FR-008**: The reopen affordance MUST NOT be shown (or must be visibly empty/disabled) when the tray holds no entries, so it does not add clutter to a session where nothing has been closed.
- **FR-009**: Selecting an entry in the reopen tray MUST reopen that panel with its original title and content/type, placed using the same clear-area placement logic (FR-002 through FR-004) as any newly-opened panel, and MUST remove the entry from the tray.
- **FR-010**: The reopen tray MUST retain at most a bounded number of most-recently-closed entries; when a new entry would exceed that bound, the oldest entry MUST be dropped.
- **FR-011**: Minimizing a panel MUST NOT add an entry to the reopen tray — minimized panels remain governed by the existing minimize/restore mechanism, unaffected by this feature.
- **FR-012**: This placement and reopen behavior MUST apply uniformly to every floating panel in the app — both content panels Lucy composes (specs/049) and live panels any viewer extension contributes (specs/050) — with no per-panel-type opt-out.
- **FR-013**: A panel reopened from the tray whose original viewer context association (layer/element) is no longer valid MUST show the same stale/invalid association indicator an already-open panel would show under the same condition, rather than failing silently or appearing as if nothing is wrong.

### Key Entities

- **Occupied Region**: A rectangle on screen the placement logic must treat as unavailable when choosing where to open a panel — covers fixed page chrome, viewer-contributed overlay widgets, and every other currently-open (non-minimized) panel's bounds.
- **Arrangement Mode**: Whether the currently open panels are laid out as a non-overlapping **grid** or an offset **cascade**, decided by whether their combined size fits the available clear area (FR-005a/FR-005b); recomputed whenever a panel opens, closes, or the viewport resizes, and on demand via the explicit "arrange" action (FR-005e).
- **Reopen Tray Entry**: A record of one closed panel retained long enough to be reopened — its title, panel kind (content vs. live), and whatever content or live-panel type/data is needed to fully reconstruct it, plus when it was closed (to order the tray and to know which entry is oldest when trimming).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When three panels are opened in quick succession without explicit positions, no two of them are ever placed at fully identical screen positions — each remains individually visible and reachable (its title bar/drag handle exposed) without first moving another panel aside, across the app's standard desktop and tablet breakpoints.
- **SC-002**: No newly-opened or reopened panel is fully hidden behind fixed page chrome (account/theme controls, viewer toolbar) at first render, across the app's standard breakpoints.
- **SC-003**: A user can restore a previously-closed panel to a fully visible, on-screen state in two interactions or fewer (open the tray, select the entry).
- **SC-004**: The reopen tray affordance is absent or visibly empty in 100% of sessions where no panel has been closed.
- **SC-005**: After a browser window resize, 100% of previously fully-visible open panels remain fully within the viewport without requiring a manual drag.
- **SC-006**: When the open panels' combined size fits the available clear area, they are arranged with zero overlapping pairs (a true grid), across the app's standard breakpoints.
- **SC-007**: When arrangement falls back to a cascade, in every overlapping pair the smaller panel is the one left reachable (frontmost), 100% of the time.
- **SC-008**: While dragging a panel, a landing placeholder is visible whenever the pointer is over a free region large enough for the panel, and hidden whenever it isn't — with no perceptible lag between pointer movement and the placeholder updating.

## Assumptions

- "Clear area" is judged against screen regions the app already knows about — fixed page chrome, viewer-contributed overlay widgets it tracks, and other open panels' own bounds — not by inspecting rendered 3D-scene pixel content; determining which patch of the underlying map/scene is visually "busy" at the pixel level is out of scope.
- The reopen tray is session-scoped only, matching the existing floating-panel store's convention (no persistence across a reload) — closed-panel entries do not survive a page reload.
- The reopen tray's bound (FR-010) defaults to the same cap already used for concurrently-open panels (10), dropping the oldest entry beyond that; this is a reasonable default, not a hard product requirement, and may be tuned during implementation.
- A reopened panel is placed fresh via the clear-area placement logic rather than restored to its exact former position, since the screen layout may have changed since it was closed.
- The reopen tray affordance's exact visual form (a slim sidebar vs. a small floating tray) is an implementation/UX decision left to the planning phase, constrained only by FR-007/FR-008 (present, reachable, and silent when empty).
- **Build vs. adopt a third-party window-manager library (investigated per explicit request)**: no existing open-source library is adopted for this feature; the grid/cascade/reopen behavior is implemented as new placement logic within the existing floating-panel system (specs/049/050 — panel-type registry, `react-rnd`-based drag/resize, Zustand store, viewer context-association). Findings:
  - **Dockview** (actively maintained, zero-dependency, the modern successor to Golden Layout) is the closest general-purpose match — it supports a "floating panel" mode alongside its docking/tabs model — but it is a full docking-layout framework that owns its own container's DOM and layout state. Adopting it would mean replacing the existing panel store/registry/content-vs-live model rather than extending it, for a large share of functionality (tabs, docked groups, popout windows) this feature doesn't need.
  - **react-grid-layout** (by far the most widely used option) auto-packs non-overlapping tiles and is a reasonable algorithmic reference for the "grid" mode, but it assumes panels are tiles filling a defined grid container — it has no concept of external occupied regions (page chrome, overlay widgets) or of a free-floating cascade mode, both of which this feature needs.
  - **golden-layout** and **rc-dock** are the same category as Dockview (tab/dock-oriented) and don't fit a canvas-overlay use case any better; Dockview is positioned as golden-layout's modern replacement.
  - **WinBox.js** is conceptually the closest to "Windows-style cascade + minimize," but its React wrapper (`react-winbox`) is effectively unmaintained (no release in ~3 years), and it's a vanilla imperative DOM library — adopting it would mean abandoning the existing declarative Zustand-driven panel state instead of extending it.
  - None of the candidates know about this app's specific "avoid this page-chrome/overlay-widget rectangle" requirement (FR-001) — that's app-specific knowledge no generic library ships with, so a library would only remove the packing/cascade math, not the integration work, while costing a rewrite of already-working, spec-049/050-tested panel infrastructure. The grid-packing idea from react-grid-layout and the cascade/size-ordering idea from WinBox/Windows inform the in-house algorithm's design, without taking on either dependency.
