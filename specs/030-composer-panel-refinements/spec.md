# Feature Specification: Composer & Panel Layout Refinements

**Feature Branch**: `030-composer-panel-refinements`

**Created**: 2026-08-20

**Status**: Draft

**Input**: User description: "Fix four UI/UX issues in the Ask Lucy chat widget's Expanded panel, refining work from specs/029-fix-chat-widget-bugs (ChatComposer.tsx, ExpandedChatPanel.tsx): (1) single-line composer should render as a rounded-corner rectangle with all controls fixed in a footer row, not the current pill with everything inline; (2) the text area should grow with content up to a maximum of ~6 lines, then stop growing and show an internal vertical scrollbar, with the footer row staying pinned at the bottom throughout; (3) the chat panel should be able to expand to full window height via a resize/toggle button, switching between half-height and full-height; (4) that resize/toggle button must sit immediately next to the existing '+' new-chat button in the panel header; (5) every icon-only button (explicitly including the mic and attachment icons) must have a tooltip describing its function. This is a UI refinement, not a behavior change — all existing composer/voice/panel functionality from specs/029-fix-chat-widget-bugs must be preserved."

## Clarifications

### Session 2026-08-20

- Q: When the user toggles the chat panel to full-window-height, should that choice persist (remembered next time they open the panel, or across page reloads), or should it always reset to half-height each time the panel is opened? → A: Persist as a preference — the last-chosen height is remembered and restored the next time the user opens the panel, even after a reload.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Composer reads as a clear input box, not a crowded pill (Priority: P1)

A user opens the chat widget and looks at the message composer before typing anything. Today it renders as a single pill-shaped bar with the text field and every control (attach, mic, mute, translate, send, etc.) squeezed into one row, which reads as cluttered and makes it unclear where to type versus where to click. The user wants the composer, when it holds a single line of text, to look like a simple rounded-corner rectangle: the typing area on top, and all the control buttons lined up in a footer row fixed to the bottom edge of that rectangle — similar to the layout used by other modern AI chat products.

**Why this priority**: This is the first thing every user sees on every visit to the chat widget; it is the visual foundation the other composer changes (auto-growing text, tooltips) build on top of.

**Independent Test**: Open the chat widget with an empty or single-line composer. Confirm the composer is a rounded rectangle (not a full pill/stadium shape) with the text entry area on top and every control button aligned along a distinct footer row at the bottom, independent of any typing or panel-resize behavior.

**Acceptance Scenarios**:

1. **Given** the chat composer is empty or holds a single line of text, **When** the user views the composer, **Then** it renders as a rectangle with rounded (not fully pill-shaped) corners.
2. **Given** the composer is in this resting state, **When** the user views it, **Then** every control button (attach, insert-prompt, mic, mode-switch, mute, translate, send) appears together in a footer row anchored to the bottom of the composer, separate from the text entry area above it.
3. **Given** the composer in this new layout, **When** the user exercises any existing control (send a message, start/stop recording, toggle mute, trigger translate, attach a file, switch voice mode), **Then** it behaves exactly as it did before this change — only the visual arrangement changed, not the behavior.

---

### User Story 2 - Composer grows with typed content, then scrolls instead of taking over the screen (Priority: P1)

A user typing a longer message watches the composer grow taller line by line, which today has no upper bound and can grow to a distracting height (observed at roughly 11 lines in a reported case). The user wants the composer to keep growing to fit their text only up to about 6 visible lines; beyond that point, the composer's height must stop increasing and the text area should scroll internally so the rest of the chat surface (message history, header, footer controls) stays visible and stable.

**Why this priority**: Directly fixes a reported usability defect (unbounded composer growth crowding out the conversation) and is tightly coupled to User Story 1's new two-row layout, since the footer row's fixed position depends on this capped-growth behavior.

**Independent Test**: Type progressively more newlines into the composer. Confirm the composer height increases up to approximately 6 lines of visible text, then stays fixed while a vertical scrollbar appears inside the text area for any additional content, with the footer button row remaining visible and stationary at the bottom of the composer at every step.

**Acceptance Scenarios**:

1. **Given** an empty composer, **When** the user types additional lines of text one at a time, **Then** the composer's visible height increases to accommodate each new line, up to a maximum of approximately 6 lines.
2. **Given** the composer already shows 6 lines of text, **When** the user types further lines, **Then** the composer's overall height no longer increases and a vertical scrollbar appears within the text area so the user can scroll to see earlier or later lines.
3. **Given** the composer is showing a scrollbar because content exceeds 6 lines, **When** the user views the footer control row, **Then** it remains fully visible, fixed at the bottom of the composer, and unaffected by the text scrolling above it.
4. **Given** the user deletes text back down to fewer than 6 lines, **When** the composer re-renders, **Then** it shrinks back down to fit the remaining content (no scrollbar, no leftover empty space), down to its single-line resting height.

---

### User Story 3 - Chat panel can expand to full window height (Priority: P2)

A user working with the expanded chat panel alongside a large content surface (e.g. the 3D viewer) wants to temporarily see much more conversation history at once. Today the expanded panel is a fixed, comparatively short size. The user wants a dedicated control that lets them expand the panel to fill the full height of the window, and switch back to the normal (half-height) size just as easily.

**Why this priority**: A valuable but self-contained enhancement — it does not block or get blocked by the composer layout changes (User Stories 1-2), so it can ship independently, but it is lower priority than fixing the two composer usability defects users already actively hit on every visit.

**Independent Test**: Open the expanded chat panel, activate the new resize/toggle control, and confirm the panel grows to occupy the full window height. Activate the control again and confirm the panel returns to its original half-height size. This can be verified without typing any message or touching the composer.

**Acceptance Scenarios**:

1. **Given** the chat panel is open at its normal (half-height) size, **When** the user activates the resize/toggle control, **Then** the panel expands to occupy the full height of the window.
2. **Given** the chat panel is expanded to full window height, **When** the user activates the resize/toggle control again, **Then** the panel returns to its original half-height size.
3. **Given** the panel is at either size, **When** the user views its contents (message history, composer, header), **Then** all existing panel functionality continues to work unchanged at both sizes.
4. **Given** the user has toggled the panel to full-window-height, **When** the user closes and reopens the panel, or reloads the page, and opens the panel again, **Then** the panel opens at full-window-height, matching the last choice they made.
5. **Given** the user has toggled the panel back to half-height, **When** the user reopens the panel later (including after a reload), **Then** the panel opens at half-height, matching that last choice.

---

### User Story 4 - Resize control sits next to the new-chat control, and every icon button explains itself (Priority: P3)

A user scanning the panel header for the new resize control should find it exactly where they'd expect: right beside the existing "+" (new conversation) button, not elsewhere in the header where it could be confused with the close/collapse control. Separately, a user unsure what an icon-only button does (the user specifically called out the microphone and attachment icons, but this applies to every icon-only control in the composer and panel header) should be able to hover or focus the button and see a short text label describing its function, rather than having to guess from the icon alone.

**Why this priority**: These are precise placement/labeling refinements on top of User Stories 1-3 — they matter for usability and accessibility but depend on those controls already existing, so they land last.

**Independent Test**: With the panel open, confirm the resize/toggle control (User Story 3) is positioned immediately adjacent to the existing new-chat "+" button in the header, on the same side, not adjacent to the close/collapse control. Separately, hover or keyboard-focus each icon-only button in the composer and panel header (at minimum: attach, mic, mode-switch, mute, translate, send, resize/toggle, new-chat, collapse) and confirm each one shows a short descriptive tooltip.

**Acceptance Scenarios**:

1. **Given** the panel header showing the new-chat "+" button, **When** the user looks at the resize/toggle button introduced in User Story 3, **Then** it appears immediately next to the "+" button, not next to the close/collapse button or elsewhere in the header.
2. **Given** any icon-only button in the composer (attach, insert-prompt, mic, mode-switch, mute, translate, send), **When** the user hovers over it with a pointer or moves keyboard focus to it, **Then** a tooltip appears describing what the button does.
3. **Given** any icon-only button in the panel header (collapse, new-chat, resize/toggle), **When** the user hovers or focuses it, **Then** a tooltip appears describing what the button does.
4. **Given** the mic icon specifically, **When** the user hovers or focuses it in any voice mode, **Then** the tooltip text reflects the mic's current contextual function (e.g. start listening vs. mute vs. push-to-talk), not a generic unchanging label.

---

### Edge Cases

- What happens if the panel is expanded to full window height while the composer's text area is also scrolled to its 6-line cap? Both behaviors must remain independently correct — the panel-height toggle must not reset or affect the composer's growth/scroll state, and vice versa.
- What happens on very narrow (mobile-width) viewports where a "full window height" panel and a 6-line composer could leave little room for message history? The panel and composer must remain usable (no control clipped or unreachable) at the platform's existing supported minimum viewport width.
- What happens if the user is actively recording voice (push-to-talk or continuous mode) when they toggle the panel to full height? The recording session must continue uninterrupted — the resize is a layout-only change.
- What happens when a tooltip's owning button is disabled (e.g. send button while composer is empty, mic while a capture error is displayed)? The tooltip must still describe the button's function (or explain why it's disabled) rather than disappearing entirely.
- What happens if the user rapidly toggles the resize/toggle button multiple times in succession? The panel must settle cleanly on the final requested state without visual glitches or getting stuck mid-transition.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The composer MUST render with rounded rectangular corners (not a full pill/stadium shape) whenever it holds one line of text or is empty.
- **FR-002**: The composer MUST present two distinct regions at all times: a text-entry area and a footer row containing every control button, with the footer row positioned at the bottom of the composer.
- **FR-003**: The composer's text-entry area MUST increase in height as the user enters additional lines of content, up to a maximum of approximately 6 visible lines.
- **FR-004**: Once the composer's content exceeds approximately 6 visible lines, the composer's overall height MUST stop increasing and the text-entry area MUST become internally scrollable (vertical scrollbar) for the overflow content.
- **FR-005**: The footer control row MUST remain fully visible and fixed at the bottom of the composer at all times, regardless of how much text is entered or whether the text area is scrolling.
- **FR-006**: When the user removes content such that it fits within fewer than 6 lines again, the composer MUST shrink back down to fit the remaining content, down to its single-line resting height.
- **FR-007**: The expanded chat panel MUST support two height states: its existing default (half-height) size, and a full-window-height size.
- **FR-008**: The expanded chat panel MUST provide a single control that toggles the panel between the half-height and full-height states, switching back and forth on repeated activation.
- **FR-008a**: The user's last-chosen panel height state (half-height or full-height) MUST be remembered and MUST be restored the next time the user opens the panel, including after closing/reopening the panel and after a full page reload.
- **FR-009**: The panel's resize/toggle control MUST be positioned in the panel header immediately adjacent to the existing new-conversation ("+") control, on the same side of the header.
- **FR-010**: Every icon-only button in the chat composer (including, at minimum, attach, insert-prompt, mic, mode-switch, mute, translate, and send) MUST expose a tooltip describing its function on hover and on keyboard focus.
- **FR-011**: Every icon-only button in the expanded panel header (including, at minimum, collapse, new-conversation, and the new resize/toggle control) MUST expose a tooltip describing its function on hover and on keyboard focus.
- **FR-012**: The mic button's tooltip text MUST reflect its current contextual function (e.g. start capture, mute, stop-and-review) consistent with whatever behavior it currently performs, rather than a single static label regardless of mode.
- **FR-013**: All tooltip text MUST be accessible to assistive technology (exposed via an accessible name or description), not rendered as a purely visual hover effect.
- **FR-014**: None of the changes in this feature MUST alter the underlying behavior of existing composer or panel functionality established in specs/029-fix-chat-widget-bugs (voice capture modes, mute/translate actions, attach/insert-prompt actions, send action, new-conversation action, panel collapse/expand-widget behavior) — this feature is limited to layout, sizing, placement, and tooltip coverage.

### Key Entities

*(Not applicable — this feature introduces no new data entities; it changes the layout, sizing behavior, and accessibility labeling of existing UI controls only.)*

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of users viewing the composer in its resting (single-line) state see a rounded-rectangle shape with a distinct bottom footer row, not the previous full-pill layout.
- **SC-002**: The composer never exceeds the height needed to display 6 lines of text, across all tested content lengths, including pasted multi-paragraph text.
- **SC-003**: 100% of icon-only buttons in the composer and panel header (a minimum set of at least 9 controls) expose a discoverable tooltip on both hover and keyboard focus.
- **SC-004**: Users can switch the chat panel between half-height and full-height in a single interaction, with the panel settling into the requested state with no visually broken intermediate state.
- **SC-004a**: A user's chosen panel height state survives a full page reload and is restored automatically the next time they open the panel, with no repeated manual toggling required.
- **SC-005**: Zero regressions: every acceptance scenario already covered by specs/029-fix-chat-widget-bugs's composer and panel test suites continues to pass unchanged after this feature ships.

## Assumptions

- "Approximately 6 lines" is interpreted as a maximum of 6 lines of the composer's default text size being visible before internal scrolling begins; the exact pixel/line-height mapping is a design/implementation detail, not a business rule.
- "Full window height" means the panel expands to fill the height of the browser viewport in which the chat widget is hosted, not the full physical screen/monitor (which would require OS-level fullscreen APIs out of scope here).
- The half-height size referenced by the new toggle is the panel's existing current default size from specs/029-fix-chat-widget-bugs (no change to that baseline size itself).
- The panel-height preference is a lightweight, per-user client-side UI setting (comparable in weight to other locally-persisted UI state already in the product) — it does not require a new backend entity or Long-Term Memory Engine record; a per-device/browser persisted value is sufficient to satisfy "remembered across reloads."
- Tooltip copy will reuse or lightly adapt each button's existing accessible label (already required as of specs/029-fix-chat-widget-bugs for several controls) rather than introducing a wholly separate content system.
- This feature applies to the expanded chat panel/composer only; the collapsed widget's own controls are out of scope unless they share a component with the expanded composer.
- No new user permissions, data, or backend changes are required — this is a client-side layout, interaction, and accessibility-labeling feature only.
