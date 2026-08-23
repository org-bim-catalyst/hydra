# Feature Specification: Transcription 500 Fix & Mode-Switch Simplification

**Feature Branch**: `032-transcription-and-mode-switch-fixes`

**Created**: 2026-08-23

**Status**: Draft

**Input**: User description: "Fix three issues in the Ask Lucy chat widget's voice/composer flow, found via live production testing: (1) transcription fails with a generic 500 in production — root-caused to OpenAIProvider only classifying 401/403/429/transient-5xx responses from OpenAI's transcription endpoint, so any other non-2xx (most plausibly a 400, triggered by a filename/codec mismatch or a too-short recording) falls through unmapped to a generic 500; (2) the Push-to-Talk mode-switch control currently requires two clicks (open a dropdown menu, then click its one option) — must become a single click that directly toggles the mode, no menu; (3) reaffirm the Push-to-Talk hold gesture (hold to record, release to finish-transcribe-and-populate) as an explicit acceptance criterion so the other two fixes don't regress it."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Voice recordings transcribe reliably instead of failing with a generic error (Priority: P1)

A user records a message using Push-to-Talk — either a quick tap-then-Finish or a press-and-hold-then-release — and expects the recording to transcribe into the message field every time speech was actually captured. Today, a real (confirmed in production) but unexplained "Transcription failed with 500" error appears instead, with no indication of what went wrong or how to avoid it, eroding trust in voice input entirely.

**Why this priority**: This is a hard failure of the core Push-to-Talk value proposition, confirmed happening in production. It blocks the primary use case this whole voice-control redesign series exists to support.

**Independent Test**: Record a short, real spoken message via Push-to-Talk (tap-then-Finish and, separately, hold-and-release) and confirm it transcribes successfully into the message field. Separately, deliberately trigger a rejection from the transcription provider (e.g., an unrecognizable/corrupt audio submission) and confirm the user sees a specific, actionable error — not an opaque generic failure — and can immediately try again.

**Acceptance Scenarios**:

1. **Given** a user records a normal, audible message via Push-to-Talk (either gesture) and the recording is long enough to contain real speech, **When** the recording finishes, **Then** it is transcribed successfully and the text appears in the message field — no generic failure.
2. **Given** the underlying transcription provider genuinely rejects a specific recording (e.g., an unsupported/corrupted audio submission), **When** that rejection occurs, **Then** the user sees a specific, visible error message reflecting that this particular recording could not be transcribed (not an unexplained generic failure), and can start a new recording immediately without reloading anything.
3. **Given** a user records recognizably real speech using Push-to-Talk, **When** the audio file is submitted for transcription, **Then** the file's format is described consistently and accurately end-to-end, so the receiving side isn't misled about what container/codec it actually contains.
4. **Given** the transcription provider is temporarily unavailable or rate-limited (an existing, already-handled condition), **When** this occurs, **Then** the user still sees the existing distinct "service unavailable"-style message, unchanged by this fix — this fix must not regress already-correct handling of those cases.

---

### User Story 2 - Switching Push-to-Talk/Continuous mode takes exactly one click (Priority: P2)

A user viewing the composer wants to switch between Push-to-Talk and Continuous conversation mode. Today, clicking the mode icon opens a dropdown menu with a single option describing the other mode, requiring a second click on that option to actually switch — an unnecessary extra step for a binary toggle.

**Why this priority**: A clear, explicitly-requested usability simplification with no functional risk, but it doesn't block anything else — it's independent of the transcription fix, so it follows the P1 reliability fix.

**Independent Test**: With the composer idle, click the mode-switch icon once. Confirm the conversation mode immediately switches (Push-to-Talk ↔ Continuous) with no intermediate menu or second click required.

**Acceptance Scenarios**:

1. **Given** the composer is in Push-to-Talk mode and idle, **When** the user clicks the mode-switch icon once, **Then** the mode immediately becomes Continuous — no dropdown/menu appears at any point during this interaction.
2. **Given** the composer is in Continuous mode, **When** the user clicks the mode-switch icon once, **Then** the mode immediately becomes Push-to-Talk — no dropdown/menu appears.
3. **Given** a Push-to-Talk recording is actively in progress, **When** the user looks at (or attempts to click) the mode-switch icon, **Then** it remains disabled exactly as it does today (mode-switching is still blocked mid-recording) — this fix changes only what a single click on the enabled control does, not the existing disabled-while-recording guard.
4. **Given** the mode has just been switched via a single click, **When** the user looks at the mode icon, **Then** it visually reflects the new current mode, exactly as it does today after a mode change.

---

### User Story 3 - The Push-to-Talk hold gesture keeps working exactly as intended (Priority: P3)

A user presses and holds the mic button, speaks, and releases it — expecting the hold itself to be the entire interaction: release immediately finishes the recording, transcribes it, and places the result in the message field, with no extra step. This is existing, already-implemented behavior; this story exists purely to confirm it still works correctly after User Stories 1 and 2 ship, since both touch code adjacent to this gesture.

**Why this priority**: Lowest priority because no change is being made here — it's a regression check on already-correct behavior, included so a regression introduced by the other two fixes would be caught immediately rather than discovered later.

**Independent Test**: In Push-to-Talk mode, press and hold the mic, speak, and release. Confirm the recording stops the instant the button is released and the transcribed text appears in the message field with no additional tap, exactly as before this round of fixes.

**Acceptance Scenarios**:

1. **Given** Push-to-Talk mode with an idle mic, **When** the user presses and holds the mic button while speaking, **Then** recording continues only for as long as the button remains held.
2. **Given** an in-progress held recording, **When** the user releases the button, **Then** recording stops immediately and the audio is transcribed into the message field as editable draft content, with no additional tap required.
3. **Given** the transcription in Scenario 2 succeeds, **When** the user reviews the text and taps Send, **Then** the message sends normally.

---

### Edge Cases

- What happens if a recording is so short (a near-instant hold-and-release, or a very brief tap-then-Finish) that there's effectively no speech to transcribe? The user must see a clear, specific error (not the generic failure this feature eliminates) rather than a confusing silent/empty result.
- What happens if the transcription provider's rejection reason changes between attempts (e.g., first attempt rejected for one reason, a retry succeeds)? Each attempt must be handled independently — a prior failure must not block a subsequent, valid recording attempt.
- What happens if the user clicks the mode-switch icon rapidly multiple times in a row? Each click must toggle the mode once, settling on a final, correct state with no visual glitch or missed toggle.
- What happens to the mode-switch icon's accessible label/tooltip (already required from specs/030) once the dropdown is removed? It must still clearly describe what a click will do (switch to the other mode), not merely name the current mode.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST successfully transcribe a Push-to-Talk recording (via either the tap-then-Finish or hold-and-release gesture) whenever the recording contains genuine, transcribable speech, without surfacing a generic/unexplained failure.
- **FR-002**: When the transcription provider rejects a specific recording as invalid or unusable, the system MUST surface a specific, visible, user-facing error describing that this recording could not be transcribed, distinct from a generic internal-error message, and MUST leave the user able to immediately attempt a new recording.
- **FR-003**: The system MUST NOT silently or genericly fail a transcription request when the underlying cause is a specific, identifiable rejection from the transcription provider (constitution §2.VIII, no silent failures) — every such failure MUST be classified and surfaced meaningfully, not defaulted to an opaque internal-error response.
- **FR-004**: The audio file submitted for transcription MUST accurately and consistently describe its own format (filename/container information) so the receiving side is never misled about what it actually contains.
- **FR-005**: This fix MUST NOT change the existing, already-correct handling of transcription-provider unavailability or rate-limiting — those cases continue to surface their existing distinct message.
- **FR-006**: The mode-switch control MUST toggle the conversation mode (Push-to-Talk ↔ Continuous) directly on a single click, with no intermediate menu, dropdown, or second interaction required.
- **FR-007**: The mode-switch control MUST remain disabled while a Push-to-Talk recording is actively in progress, exactly as it does today — this feature does not change that guard.
- **FR-008**: The mode-switch control's accessible label/tooltip MUST continue to clearly describe its action after the dropdown is removed.
- **FR-009**: The Push-to-Talk hold gesture (press-hold-release → immediate finish, transcribe, and populate the message field) MUST continue to function exactly as already implemented, unaffected by FR-001–FR-008.

### Key Entities

*(Not applicable — this feature fixes error-handling/classification and simplifies an interaction; it introduces no new data entities.)*

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Push-to-Talk recordings containing genuine, audible speech transcribe successfully in production, with zero occurrences of the generic "Transcription failed with 500" message for that case.
- **SC-002**: When a recording is genuinely rejected by the transcription provider, 100% of the time the user sees a specific, actionable message (not a generic internal-error message) and can retry immediately.
- **SC-003**: Switching conversation mode takes exactly one click/tap, 100% of the time, with zero intermediate menus.
- **SC-004**: Zero regressions: the hold-to-talk gesture (User Story 3) and the already-correct provider-unavailable/rate-limited handling (FR-005) continue to pass every acceptance scenario already covered by specs/029/030/031's test suites.

## Assumptions

- The most plausible triggers for the underlying 400 (filename/codec mismatch between what's sent and the browser's actual recording format, and/or a too-short/near-silent recording) are both addressed by this fix's scope; if production logs later reveal a different, additional 4xx cause from the transcription provider once this ships, that is a follow-up, not a reason to block this fix — the core requirement (FR-002/FR-003: no un-classified failure defaults to a generic 500) holds regardless of the specific rejection reason.
- "Specific, visible, user-facing error" (FR-002) reuses this project's existing error-surfacing pattern for transcription failures (the same visible error path `useVoiceRecorder`'s `error` state and its Snackbar already use) — this feature does not introduce a new error-display mechanism, only ensures more failure cases reach it with an accurate, specific message instead of a generic one.
- Removing the mode-switch dropdown (User Story 2) does not remove any capability — both modes remain reachable, just via one click instead of two — so no functionality is lost, only a step.
- No new user permissions, external integrations, or database changes are required.
