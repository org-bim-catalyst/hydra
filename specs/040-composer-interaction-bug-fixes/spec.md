# Feature Specification: Composer Interaction Bug Fixes

**Feature Branch**: `040-composer-interaction-bug-fixes`

**Created**: 2026-08-25

**Status**: Draft

**Input**: User description: "Fix a set of composer UI/UX bugs discovered during live browser verification of specs/039-composer-interaction-states-redesign: wrong button positions in the empty, typing, recording-review, and continuous-conversation composer states; continuous conversation silently failing to start listening on first entry; transcription requests failing with an unhelpful generic 500 error; and inconsistent tooltip placement. Each issue is to be delivered and merged as its own independent PR, in priority order."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Empty-state button positions (Priority: P1)

A user opens a conversation with Ask Lucy and sees the composer in its empty (no text typed) state. The attachment button sits at the far left of the composer's control row, and the microphone and continuous-conversation buttons sit together at the far right — matching the reference mockup (Figure 1) — rather than all three controls being bunched together at the left edge with empty space on the right.

**Why this priority**: This is the composer's default, most-frequently-seen state — every session starts here, and the misplaced controls are immediately visible to every user on every visit.

**Independent Test**: Open the composer with no text typed and no recording active; visually/structurally verify the attachment control anchors the left edge and the microphone + continuous-conversation controls anchor the right edge, with the gap between them, not after them.

**Acceptance Scenarios**:

1. **Given** the composer is empty and idle, **When** it renders, **Then** the attachment control is the leftmost element in the control row and the microphone and continuous-conversation controls are the rightmost elements, with the available space between the two groups.
2. **Given** the composer is empty and idle, **When** the window is resized narrower or wider, **Then** the attachment control stays pinned left and the microphone/continuous-conversation controls stay pinned right at every width.

---

### User Story 2 - Typing-state composer keeps attach and mic visible (Priority: P1)

A user starts typing a message. The composer's control row continues to show the attachment button and the microphone button alongside the Send button, matching the reference mockup (Figure 2), instead of hiding the attachment and microphone controls entirely and showing only Send.

**Why this priority**: Losing the ability to attach a file or start a voice recording the moment any text is typed is a functional regression that blocks a common workflow (e.g., "type a note, then also attach a file" or "start typing, then finish the thought by voice"), not just a cosmetic issue.

**Independent Test**: Type any non-empty text into the composer; verify the attachment control, the microphone control, and the Send control are all present and each remains fully operational (attachment opens the file picker; microphone starts a recording whose transcript appends after the existing typed text; Send sends the current text).

**Acceptance Scenarios**:

1. **Given** the composer is empty, **When** the user types a character, **Then** the attachment and microphone controls remain visible and the continuous-conversation control is replaced by Send.
2. **Given** the composer has typed text, **When** the user starts and finishes a voice recording via the still-visible microphone control, **Then** the transcribed text is appended after the existing typed text (not replacing it), consistent with the existing empty-state append behavior.
3. **Given** the composer has typed text, **When** the user clears all the text, **Then** the composer returns to the empty-state control layout (User Story 1).

---

### User Story 3 - Recording/tap-review button order (Priority: P1)

A user taps the microphone to start a click-to-talk recording, then releases quickly (a tap, not a hold). The review controls that appear show the cancel (discard) control to the left of the live waveform and the finish (confirm) control to the right of it, matching the reference mockup (Figure 3), instead of showing the waveform first with both controls bunched after it in finish-then-cancel order.

**Why this priority**: A misordered discard/confirm pair is a correctness-adjacent usability risk — a user reaching for "confirm" in the position they expect can hit "cancel" instead and lose their recording.

**Independent Test**: Start a click-to-talk recording (a tap release under the hold threshold); verify the awaiting-review control row reads, left to right: cancel (X), waveform, finish (check). Verify clicking each control still performs its existing action (cancel discards and returns to the empty state; finish transcribes and appends to the composer text).

**Acceptance Scenarios**:

1. **Given** the user taps the microphone and releases quickly, **When** the recording-review controls appear, **Then** the cancel control renders to the left of the waveform and the finish control renders to the right of the waveform.
2. **Given** the recording-review controls are showing, **When** the user activates the cancel control, **Then** the recording is discarded and the composer returns to the empty state, unchanged from current behavior.
3. **Given** the recording-review controls are showing, **When** the user activates the finish control, **Then** the recording is transcribed and appended to the composer's text, unchanged from current behavior.

---

### User Story 4 - Continuous-conversation composer shows a live waveform with mute/exit on the right (Priority: P2)

While continuous conversation mode is actively listening, the composer's control row shows a live waveform occupying the left/majority of the row, with the mute and exit controls anchored to the right, matching the reference mockup (Figure 4) — instead of showing no waveform at all and the mute/exit controls anchored to the left.

**Why this priority**: Lower priority than US1–3 because continuous mode is used less often than typing/basic recording, but it's still a visible, every-session-for-that-mode defect and it also removes useful listening feedback (the waveform).

**Independent Test**: Enter continuous conversation mode and let it reach its idle-listening state; verify a live waveform renders and fills the leading space of the control row, with the mute and exit controls anchored at the trailing edge.

**Acceptance Scenarios**:

1. **Given** continuous conversation mode is active and idle-listening, **When** the composer renders, **Then** a live waveform is visible and occupies the leading portion of the control row.
2. **Given** continuous conversation mode is active and idle-listening, **When** the composer renders, **Then** the mute and exit controls are anchored to the trailing edge of the control row, to the right of the waveform.
3. **Given** continuous conversation mode is active, **When** the user mutes/unmutes or exits, **Then** the existing behavior of those controls is unchanged — only their position and the presence of the waveform change.

---

### User Story 5 - Continuous conversation reliably starts listening (Priority: P1)

A user enters continuous conversation mode for the very first time in a session, before any conversation/chat exists yet. The microphone actually starts listening once the mode is active, without requiring the user to first send a typed message as an unrelated workaround.

**Why this priority**: This is a functional failure of the feature's core promise ("continuous conversation" that doesn't converse), and it fails silently with no error shown to the user — directly contradicting the product's no-silent-failures requirement.

**Independent Test**: With no existing conversation open, activate continuous conversation mode from the empty composer state; verify listening begins (the microphone becomes active and produces a visible listening indication) without any further user action, or, if listening genuinely cannot start, verify a visible error is shown explaining why.

**Acceptance Scenarios**:

1. **Given** no conversation exists yet, **When** the user activates continuous conversation mode, **Then** listening starts as soon as the prerequisites it depends on (e.g., an active conversation to attach the turn to) become available, without any additional user action beyond entering the mode.
2. **Given** continuous conversation mode is active and listening has started, **When** the user speaks, **Then** the existing listen/respond behavior proceeds exactly as it does when a conversation already existed beforehand.
3. **Given** listening genuinely cannot start (e.g., microphone permission denied, or a real error from the underlying voice engine), **When** the user activates continuous conversation mode, **Then** a visible, specific error is shown — never a silent no-op.

---

### User Story 6 - Transcription failures surface a classified, actionable error (Priority: P1)

When transcribing a voice recording (e.g., finishing a hold-to-talk or click-to-talk recording) fails, the user sees a specific, actionable error message appropriate to the actual cause (e.g., "the AI provider rejected the configured credential" for a credential problem) instead of a generic "an unexpected error occurred" message that gives no indication of what went wrong or what to do about it.

**Why this priority**: Voice input is one of the composer's primary interaction modes; a failure here currently looks identical to an unrelated server crash, giving users and administrators no actionable signal, which directly violates the product's no-silent-failures requirement.

**Independent Test**: Trigger a transcription failure (e.g., by simulating an upstream failure in a lower environment, or by reviewing server-side classification logic/tests); verify the response the user sees reflects the true cause and category of the failure rather than the generic catch-all message, and that the failure is logged server-side with enough detail to diagnose it.

**Acceptance Scenarios**:

1. **Given** the transcription request fails because the configured AI provider credential is missing or invalid, **When** the failure occurs, **Then** the user sees the existing "provider rejected the configured credential" message (not the generic catch-all), and the condition is logged server-side.
2. **Given** the transcription request fails for a reason that isn't one of the already-classified categories (authentication, rate limiting, invalid request), **When** the failure occurs, **Then** it is still classified into an appropriate, actionable category rather than falling through to the unclassified generic response.
3. **Given** a transcription request succeeds, **When** the response is processed, **Then** behavior is unchanged from today.

---

### User Story 7 - Consistent bottom-positioned tooltips (Priority: P3)

When a user hovers over or focuses any button in the composer or its voice controls, the tooltip that appears is positioned below the button, consistently across every control, instead of some tooltips appearing to the left or right while others appear elsewhere.

**Why this priority**: Purely cosmetic/consistency polish with no functional impact — lowest priority, appropriate to ship last.

**Independent Test**: Hover/focus every button in the composer's control row and in the recording-review and collapsed voice-control layouts; verify every tooltip appears below its control.

**Acceptance Scenarios**:

1. **Given** any composer or voice-control button, **When** it is hovered or focused, **Then** its tooltip appears positioned below the button.

---

### Edge Cases

- What happens if the composer's available width is too narrow to show all controls for a given state (e.g., a very narrow collapsed panel)? Existing responsive/overflow behavior for the control row is preserved; only the ordering/anchoring described above changes.
- What happens if a user starts typing while a click-to-talk recording is awaiting review (US3)? Recording takes priority over typed text exactly as it does today (unchanged) — this feature does not change that precedence.
- What happens if continuous conversation mode is toggled on and off rapidly before the prerequisite conversation becomes available (US5)? The most recent toggle state wins; no listening session is left running after the user has exited the mode.
- What happens if the transcription failure is transient and a retry would succeed (US6)? Existing retry-before-classifying behavior for transient failures is preserved; only the final classification of a failure that survives retries changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: In the empty composer state, the system MUST anchor the attachment control to the leading edge of the control row and the microphone and continuous-conversation-entry controls to the trailing edge.
- **FR-002a**: In the typing composer state while in Push-to-Talk mode (Figure 2), the system MUST display the attachment control at the leading edge, the microphone and Send controls at the trailing edge, with the spacer between the two groups — the continuous-conversation-entry control is hidden.
- **FR-002b**: In the typing composer state while in Continuous conversation mode (Figure 5), the system MUST display only the attachment control at the leading edge and the Send control at the trailing edge — the microphone control is NOT shown (it is already active in the background).
- **FR-003**: In the Push-to-Talk typing state, the microphone control MUST remain fully functional (click-to-talk and hold-to-talk), with a transcribed result appended after the existing typed text.
- **FR-004**: In the recording-review (tap-release-awaiting-confirmation) composer state, the system MUST render the cancel control before the live waveform and the finish control after the live waveform, in that left-to-right order.
- **FR-005**: In the continuous-conversation idle-listening composer state, the system MUST render a live waveform occupying the leading portion of the control row.
- **FR-006**: In the continuous-conversation idle-listening composer state, the system MUST anchor the mute and exit controls to the trailing edge of the control row.
- **FR-007**: The system MUST start listening in continuous conversation mode once its prerequisites become available after the mode is activated, without requiring an unrelated user action (such as sending a typed message) as a workaround.
- **FR-008**: The system MUST surface a visible, specific error if continuous conversation mode genuinely cannot start listening, rather than remaining silently inactive.
- **FR-009**: The system MUST classify transcription failures into the existing actionable failure categories (e.g., provider-authentication-failed, provider-rate-limited, provider-request-invalid, provider-unavailable) whenever the underlying cause matches one of those categories, rather than returning the generic unclassified failure response.
- **FR-010**: The system MUST log enough detail server-side about any transcription failure — classified or not — to diagnose its root cause.
- **FR-011**: Every button tooltip in the composer, recording-review, and voice-control layouts MUST be positioned below its control.

### Key Entities

- **Composer Control Row**: The row of interactive controls (attachment, microphone, continuous-conversation entry/exit, mute, send, cancel/finish) whose visible membership and left/right anchoring depends on the composer's current interaction state.
- **Transcription Failure Classification**: The mapping from an underlying transcription failure cause to a specific, actionable, user-facing error category.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a visual/structural check of each composer state (empty, typing, recording-review, continuous idle-listening), 100% of control positions match the reference mockups.
- **SC-002**: Attaching a file or starting a voice recording is possible from the composer at any time text is present, with 0% of previously-available actions lost while typing.
- **SC-003**: Continuous conversation mode successfully starts listening on first activation in a brand-new session in 100% of cases where the microphone is available and permitted, with no manual workaround required.
- **SC-004**: 100% of transcription failures that match an existing classified failure category surface that category's specific message to the user, rather than the generic catch-all message.
- **SC-005**: 100% of button tooltips across the composer and voice controls appear below their control.

## Assumptions

- The reference mockups already reviewed (docs/UI-UX-Functional-Requirements.md and docs/images/figure-image-{1,2,3,4,9,11}.png) remain the source of truth for control positions; no new mockups are introduced by this feature.
- "Reliably starts listening" (US5) means the existing continuous-conversation listening mechanism is triggered correctly once its real prerequisites are met — this feature does not change what those prerequisites are (e.g., it does not remove the need for an active conversation), only ensures the trigger isn't silently dropped when they aren't met at the moment the mode is activated.
- US6's scope is limited to failure classification and diagnostics (surfacing the right category of error and logging enough to diagnose it) — this feature does not attempt to fix an underlying AI-provider outage or credential problem itself, since that is an operational/configuration concern outside the codebase.
- Each user story is delivered as its own independently mergeable change; later-priority stories do not block earlier ones, and the relative order (US1 → US7) is the intended delivery sequence but not a hard technical dependency between stories.
- No new user-facing copy/strings beyond what's already referenced (e.g., existing error messages already defined in the codebase) needs to be authored from scratch for this feature.
