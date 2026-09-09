# Feature Specification: Reply Action Bar

**Feature Branch**: `046-reply-action-bar`

**Created**: 2026-09-09

**Status**: Draft

**Input**: User description: "Redesign the assistant-reply action bar in the Chat UI (MessageBubble.tsx). Currently the replay/stop control floats inside the message bubble at its lower-right corner. Add a new 'Copy' action next to Replay that copies the message's text content to the clipboard, and move both actions out of the bubble to sit below it, left-aligned under the bubble (matching the ChatGPT/Claude convention of an action row beneath the assistant's response), using the same icon library already in use (remixicon, @remixicon/react) at a slightly smaller size than the current replay icon. This only applies to assistant replies, not user messages. Preserve all existing replay/stop behavior and rules from specs/039-composer-interaction-states-redesign (single active playback, disabled during initial speaking/mute, restart-from-beginning). Copy action shows a brief visible success/failure confirmation per the project's no-silent-failure error handling policy."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Copy a reply's text (Priority: P1)

As a user reading an assistant reply, I want to copy its text with one click so I can paste it elsewhere without manually selecting text.

**Why this priority**: Copy is the most common action users take on an AI reply and is table-stakes in comparable products (ChatGPT, Claude); it is also entirely new functionality, so it delivers the most net-new value.

**Independent Test**: Can be fully tested by viewing any completed assistant reply, activating its Copy action, and confirming (a) the reply's exact text is on the clipboard and (b) a visible confirmation appears — independent of whether replay/voice is wired up at all.

**Acceptance Scenarios**:

1. **Given** a completed assistant reply, **When** the user activates its Copy action, **Then** the reply's text content is placed on the system clipboard and a brief visible success confirmation appears.
2. **Given** the clipboard write fails (e.g., browser denies permission), **When** the user activates Copy, **Then** a visible failure indication appears and no silent no-op occurs.
3. **Given** a user's own message (not an assistant reply), **When** the user views it, **Then** no Copy action is shown for it.

---

### User Story 2 - Reply actions read as a single, familiar row (Priority: P2)

As a user, I want an assistant reply's actions (Replay, Copy) to appear as a clean row beneath the reply — not floating on top of the message text — so the interface matches the layout convention of other mainstream AI chat products I already know.

**Why this priority**: This is a visual/placement change to an already-shipped control (specs/039); it improves consistency and readability but changes no underlying playback behavior, so it's lower risk/value than adding Copy itself.

**Independent Test**: Can be fully tested by viewing an assistant reply and confirming the action row renders below the message bubble, left-aligned, with icons visibly smaller than the previous in-bubble control, without needing to exercise playback.

**Acceptance Scenarios**:

1. **Given** an assistant reply that is eligible for a replay control, **When** the user views it, **Then** the Replay and Copy actions appear together in a row directly beneath the message bubble (not overlapping the bubble text), left-aligned under it.
2. **Given** the reply action row, **When** compared to the prior in-bubble control, **Then** the action icons render at a visibly smaller size while remaining easily clickable.
3. **Given** an assistant reply with no replay control offered (e.g., replay not wired up in the current view), **When** the user views it, **Then** only the Copy action appears in the row (the row still appears below the bubble, not inside it).

---

### User Story 3 - Replay/stop behavior is unchanged (Priority: P1)

As a user who already relies on the reply replay/stop control, I want its behavior to keep working exactly as before after the layout change, so relocating the control doesn't regress functionality I depend on.

**Why this priority**: Regressing already-shipped, spec'd behavior (specs/039-composer-interaction-states-redesign) is the highest-risk outcome of this change and must be explicitly protected.

**Independent Test**: Can be fully tested by exercising the existing specs/039 User Story 5 acceptance scenarios (play, stop, restart-from-beginning, single-active-playback, disabled during initial speaking/mute) against the relocated control.

**Acceptance Scenarios**:

1. **Given** an assistant reply is not currently speaking or muted, **When** the user views the reply, **Then** the relocated Replay action is visible and enabled in a "play" appearance.
2. **Given** the assistant is currently speaking a reply for the first time, or audio is muted, **When** the user views that reply's Replay action, **Then** it is disabled.
3. **Given** the user activates an enabled Replay action, **When** playback starts, **Then** the action switches to a "stop" appearance and any other currently-playing reply's audio stops first.
4. **Given** a reply is replaying, **When** the user activates the "stop" action, **Then** playback stops immediately and the action returns to "play".
5. **Given** a reply's playback was stopped partway through, **When** the user activates Replay again, **Then** playback restarts from the beginning.

### Edge Cases

- What happens when the reply's text content contains Markdown/KaTeX source rather than rendered output — does Copy place the raw source or the rendered plain text on the clipboard? (See Assumptions.)
- What happens when Copy is activated twice in quick succession on the same reply, or on two different replies back-to-back? Each activation MUST independently attempt the copy and show its own confirmation; a later Copy activation is not blocked by a still-visible confirmation from a prior one.
- What happens on a reply that is still streaming (no stable message id yet)? The action row (both Copy and Replay) MUST NOT appear until the reply is complete, consistent with the existing rule that only a completed reply gets a replay control.
- What happens when the browser/environment has no Clipboard API available or permission is denied? This MUST surface as a visible failure per User Story 1 Scenario 2, never a silent no-op.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST display a Copy action for every completed assistant reply (a reply with a stable id), never for user messages.
- **FR-002**: Activating the Copy action MUST place the reply's message text on the system clipboard.
- **FR-003**: The system MUST show a brief, visible confirmation when a Copy action succeeds, and a distinct, visible indication when it fails — a copy attempt MUST NOT fail or succeed silently.
- **FR-004**: The system MUST render the assistant reply's action row (Replay when offered, plus Copy) beneath the message bubble, not overlapping or floating inside the message content area.
- **FR-005**: The reply action row MUST be left-aligned beneath the bubble.
- **FR-006**: The action icons in the reply action row MUST use the same icon library already used for the existing replay/stop icons, at a visibly smaller size than the prior in-bubble control used.
- **FR-007**: The system MUST NOT render any reply action row (Copy or Replay) for a reply that has not yet completed (no stable message id).
- **FR-008**: The relocated Replay/Stop action MUST continue to satisfy every functional requirement defined in specs/039-composer-interaction-states-redesign FR-020 through FR-025 (visibility conditions, disabled-while-speaking-or-muted, single-active-playback across replies, stop behavior, restart-from-beginning) — this feature changes only the control's position and size, not its behavior.
- **FR-009**: When a reply offers no Replay control (e.g., replay not wired up by the caller), the action row MUST still render beneath the bubble showing only the Copy action.

### Key Entities

- **Reply Action Row**: The row of per-reply actions (Copy, Replay/Stop) rendered beneath a completed assistant message bubble; replaces the prior single in-bubble replay control.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can copy any completed assistant reply's text in a single interaction, with a visible confirmation appearing within 1 second of activation.
- **SC-002**: 100% of completed assistant replies display their action row beneath the bubble rather than overlapping the message text.
- **SC-003**: Every existing specs/039 replay/stop acceptance scenario continues to pass unchanged after this feature ships (zero regressions).
- **SC-004**: No copy attempt ever completes without a visible outcome (success or failure) reaching the user.

## Assumptions

- Copy places the reply's plain-text message content (the same source string rendered by Markdown, i.e. `message.content`) on the clipboard, not a re-serialized/re-rendered version — this matches what "copy the text in her answers" most naturally means and requires no extra rendering step.
- "Same icon library" means the existing `@remixicon/react` package already used for `RiPlayFill`/`RiStopFill`; a copy icon from that same library (e.g. its copy/clipboard glyph) is used for visual consistency.
- The action row sits directly beneath the bubble as normal document flow (not absolutely positioned over it), so it participates in the message list's spacing rather than overlapping content — matching the "same look and feel as Claude and ChatGPT" reference given by the user.
- This feature is a targeted amendment to the reply action control introduced by specs/039-composer-interaction-states-redesign; it does not revisit or change any voice/transcription/recording behavior outside the replay control itself.
- User messages (the user's own chat bubbles) are out of scope — no action row is added to them.
