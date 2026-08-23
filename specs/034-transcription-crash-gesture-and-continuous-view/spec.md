# Feature Specification: Transcription Crash Fix, Review-Gesture Restoration & Continuous Voice View

**Feature Branch**: `034-transcription-crash-gesture-and-continuous-view`

**Created**: 2026-08-23

**Status**: Draft

**Input**: User description: "Three issues from further live production testing after SPEC-032/033: (1) transcription still fails with a generic 500 even after two rounds of backend classification fixes — a fresh, from-scratch investigation (not assuming the prior fixes' target area) found the actual cause is a third, previously-unexamined gap: AiController's Transcribe/TranscribeMicrophone actions call file.OpenReadStream() on an IFormFile parameter with no null-check, and ASP.NET Core's model binder silently leaves IFormFile null (rather than failing validation) when a multipart upload's file part is missing/malformed (a flaky connection, an aborted request, a mangled boundary) — producing an uncaught NullReferenceException that falls through ProblemDetailsMiddleware's default 500 case, whose exact detail text matches the reported toast byte-for-byte. The same investigation also found that production logging is effectively non-functional: Serilog only has a Console sink, and the generated web.config disables ANCM stdout capture (stdoutLogEnabled=false), so the real exception behind all three production occurrences was logged into a void and is currently unrecoverable — this must be fixed too, or any future recurrence will be equally undiagnosable. (2) The user has revised the intended Push-to-Talk gesture model, superseding SPEC-033's pure-hold-only design: a single click on the mic button must start recording AND show the ✓ (transcribe) / ✗ (discard) review controls plus the live analyzer/waveform, requiring an explicit follow-up tap to complete or discard; a genuine press-and-hold must show only the analyzer (no review buttons) while held, and release must automatically stop, transcribe, and populate the message field with no further tap needed — both gestures live on the same control. (3) The user wants Continuous conversation mode to become a dedicated, distinct full view (not just a change in composer behavior) the moment the mode-switch button is clicked into Continuous mode, modeled on how ChatGPT and Claude's own voice-mode UIs work: a focused view showing only Lucy's reactive presence visualization plus exactly two controls, Exit (leaves the view, stops the live session) and Mute (mutes Lucy's spoken output) — no other controls or the normal text composer visible in this view."

## Clarifications

### Session 2026-08-23

- Q: Should the dedicated Continuous voice view open automatically whenever a chat with Continuous as its saved mode preference loads, or only on an explicit user action? → A: Explicit action only. Loading/opening a chat never auto-enters the dedicated view or auto-prompts for microphone permission, even if Continuous is the saved preference — the view opens only when the user actively switches into Continuous mode (e.g., the mode-switch button) or otherwise explicitly starts a voice session.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Voice recordings never crash the server with an unexplained error, and any future failure is diagnosable (Priority: P1)

A user records a voice message and expects it to either transcribe successfully or, if something goes wrong, show a specific, actionable message — never an opaque "unexpected error." This has now failed identically three times in production despite two rounds of fixes, because the actual cause was a request-handling gap unrelated to either prior fix, and because production logging was silently broken the entire time, meaning nobody — including the support/engineering side — could actually see what was going wrong.

**Why this priority**: This is the same production-blocking failure reported across three consecutive rounds. Fixing the code path without also fixing the logging blackout would leave the team unable to confirm the fix worked or diagnose whatever comes next — both halves are required for this to actually be closed.

**Independent Test**: Simulate a malformed/missing file part in a transcription upload (e.g., a request with no `file` field, or a truncated multipart body) and confirm the server responds with a specific, non-generic error rather than a 500. Separately, confirm that an unhandled server-side exception is now actually retrievable (not silently dropped) by the operator after the fact.

**Acceptance Scenarios**:

1. **Given** a transcription upload request arrives with a missing or unreadable audio file part, **When** the server processes it, **Then** the user sees a specific, actionable error (not a generic "unexpected error" message), and the request never reaches an uncaught exception.
2. **Given** a normal, well-formed voice recording is uploaded, **When** it is transcribed, **Then** behavior is unchanged from today — this fix only affects the malformed-upload case, not the success path.
3. **Given** any unhandled exception occurs anywhere in the request pipeline after this fix ships, **When** it happens, **Then** the exception's real detail is captured somewhere an operator can actually retrieve after the fact — not written to a log destination that production silently discards.

---

### User Story 2 - Push-to-Talk supports both a reviewable tap-to-record flow and a direct hold-to-talk flow, on the same control (Priority: P1)

A user interacting with the Push-to-Talk mic button expects two distinct, deliberate ways to record: a single click that starts recording and waits for their explicit confirmation (via a checkmark) or discard (via a cancel), with a live waveform visible throughout; or a press-and-hold that records only while held and finishes automatically the instant they let go, with no separate confirmation step. This supersedes the prior round's simplification to hold-only, which removed the reviewable click flow the user actually wants back.

**Why this priority**: Directly, explicitly requested as a correction to the immediately preceding round's behavior — this blocks the core Push-to-Talk experience exactly as much as the transcription failure does, since neither of the two intended interaction patterns currently works as wanted.

**Independent Test**: Click the mic once (no hold) — confirm recording starts, a waveform and both a checkmark and a cancel control appear, and nothing is sent until one of those two controls is used. Separately, press and hold the mic, speak, and release — confirm only the waveform is shown throughout (no checkmark/cancel), and release alone transcribes and populates the message field with no further tap.

**Acceptance Scenarios**:

1. **Given** Push-to-Talk mode with an idle mic, **When** the user clicks the mic button once (a tap, not a hold), **Then** recording starts and both a confirm (✓) control and a discard (✗) control appear alongside a live waveform, and recording continues until the user acts on one of them.
2. **Given** a recording started by a tap is active, **When** the user taps the confirm (✓) control, **Then** the recording stops, is transcribed, and the result populates the message field as editable draft text.
3. **Given** a recording started by a tap is active, **When** the user taps the discard (✗) control, **Then** the recording stops immediately and is discarded — nothing is transcribed or sent.
4. **Given** Push-to-Talk mode with an idle mic, **When** the user presses and holds the mic button while speaking, **Then** recording continues only for as long as the button is held, and only the live waveform is shown — no confirm/discard controls appear during a hold.
5. **Given** a held recording is in progress, **When** the user releases the button, **Then** recording stops immediately and is transcribed automatically, with the result populating the message field — no additional tap of any kind is required.
6. **Given** the user is holding the button, **When** they have not yet released it, **Then** the interaction so far behaves exactly like Scenario 4 regardless of how long the hold lasts — a hold is never reinterpreted as a tap partway through.

---

### User Story 3 - Continuous conversation mode opens a dedicated, focused voice view (Priority: P1)

A user who switches into Continuous conversation mode expects the interface to transform into a focused voice-conversation experience — similar to ChatGPT's or Claude's voice mode — showing Lucy's reactive presence prominently, with exactly two controls available: one to exit back to the normal chat view, and one to mute Lucy's spoken responses. The ordinary text composer and its other controls are not part of this view.

**Why this priority**: Explicitly requested as core UX parity with the reference products the user is comparing against; it materially changes how Continuous mode is experienced and was raised with the same urgency as the other two production-blocking issues.

**Independent Test**: From the normal chat view, click the mode-switch button to activate Continuous mode. Confirm the view changes to a focused voice view showing Lucy's reactive presence and exactly two controls (Exit, Mute), with no text composer or other controls visible. Confirm Exit returns to the normal chat view and stops the live listening session, and Mute silences Lucy's spoken output without leaving the view.

**Acceptance Scenarios**:

1. **Given** the user is in the normal chat view in Push-to-Talk mode, **When** they click the mode-switch button to activate Continuous mode, **Then** the interface transitions to a dedicated voice view showing Lucy's reactive presence visualization and exactly two controls: Exit and Mute.
2. **Given** the dedicated voice view is active, **When** the user looks at the available controls, **Then** the normal text composer (message field, attach, insert-prompt, send) is not visible or reachable in this view.
3. **Given** the dedicated voice view is active and Lucy is speaking, **When** the user taps Mute, **Then** her spoken output stops/silences immediately, the view remains open, and the control reflects the muted state.
4. **Given** the dedicated voice view is active, **When** the user taps Exit, **Then** the live conversation session stops and the interface returns to the normal chat view.
5. **Given** the user has exited the voice view, **When** they view the conversation, **Then** any exchanges that occurred while in the voice view are present in the message history, exactly as if they had occurred in the normal view.

---

### Edge Cases

- What happens if the user starts a tap-recording (Scenario 1 in US2) and then, instead of tapping ✓/✗, presses and holds the same button again without releasing first? The existing recording session continues to behave as a tap-initiated recording (confirm/discard still apply) — a second press on an already-recording control does not start a new, independent hold-session.
- What happens if the multipart upload gap from US1 occurs mid-transcription rather than at the start (e.g., the connection drops partway through)? The same specific, non-generic error handling applies — the fix is not limited to a request that never had a file part at all.
- What happens if the user activates Continuous mode's dedicated view while a Push-to-Talk recording is already in progress? The existing guard that blocks mode-switching during an active recording (specs/032) continues to apply — the dedicated view only opens once any in-progress recording has been resolved.
- What happens to Lucy's mute state (US3 Scenario 3) after the user exits and later re-enters the voice view? The mute preference persists exactly as today's existing speaker-mute setting already does — this feature does not change that persistence behavior, only where the control is surfaced.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST NOT crash with an unhandled/unclassified error when a transcription upload's file part is missing, unreadable, or truncated — this MUST be surfaced as a specific, actionable error distinct from a generic "unexpected error" message.
- **FR-002**: This fix MUST NOT change behavior for a normal, well-formed voice recording upload.
- **FR-003**: The system MUST ensure that any future unhandled server-side exception is retrievable by an operator after the fact, not silently discarded by the production logging configuration.
- **FR-004**: The Push-to-Talk mic control MUST support two distinct gestures on the same button: a tap (click) that starts recording and shows confirm/discard controls plus a live waveform, requiring explicit user action to complete; and a press-and-hold that records only while held, shows only the live waveform (no confirm/discard controls), and automatically completes on release.
- **FR-005**: A tap-initiated recording MUST NOT auto-complete on its own — it MUST remain active, showing confirm/discard controls, until the user explicitly confirms or discards it.
- **FR-006**: A hold-initiated recording MUST NOT show confirm/discard controls at any point during the hold, and MUST automatically stop, transcribe, and populate the message field the instant the button is released, with no further user action required.
- **FR-007**: Since a tap and a hold begin identically (the same press) and are only distinguishable by what the user does next, the system MUST show only the live waveform for the duration of every press (before either gesture is known to have completed), and MUST decide which gesture occurred at the point of release: a quick release resolves to the tap flow (Scenario 1-3, confirm/discard controls appear now that the tap is complete), while a release that occurs after the button has already been held long enough to count as a hold resolves directly to the automatic-completion flow (Scenario 5) — a gesture already resolved as a hold MUST NOT retroactively show confirm/discard controls.
- **FR-008**: Activating Continuous conversation mode via an explicit user action (the existing mode-switch control, or an equivalent deliberate action) MUST transition the interface to a dedicated voice view distinct from the normal chat view. Loading or opening a chat MUST NOT automatically enter this view on its own, even when Continuous is the user's saved mode preference (Clarification, 2026-08-23) — no unrequested microphone-permission prompt or live session start on page load.
- **FR-009**: The dedicated voice view MUST show Lucy's reactive presence visualization and exactly two controls: one to exit the view, and one to mute/unmute Lucy's spoken output.
- **FR-010**: The dedicated voice view MUST NOT show the normal text composer or its other controls (attach, insert-prompt, send, mode-switch).
- **FR-011**: Exiting the dedicated voice view MUST stop the live listening/conversation session and return the interface to the normal chat view.
- **FR-012**: Muting from within the dedicated voice view MUST use the same mute mechanism/state already governing Lucy's spoken output elsewhere in the product — this feature does not introduce a second, independent mute concept.
- **FR-013**: Conversation exchanges that occur while the dedicated voice view is active MUST appear in the persisted message history identically to exchanges in the normal view.
- **FR-014**: This feature MUST NOT change the already-correct handling from prior rounds of: transcription-provider unavailability/rate-limiting/explicit-rejection responses (specs/032), the malformed-2xx-response classification (specs/033), or the mode-switch-blocked-during-recording guard (specs/032).

### Key Entities

*(Not applicable — this feature fixes a request-handling gap, restores/refines an interaction gesture, and adds a UI view mode; it introduces no new data entities.)*

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of transcription requests with a missing/malformed file part receive a specific, actionable error response — zero occurrences of the generic "unexpected error" message for that case.
- **SC-002**: 100% of well-formed voice recordings continue to transcribe successfully, matching pre-fix behavior exactly.
- **SC-003**: An unhandled server-side exception occurring after this fix ships is retrievable by an operator within one operational check (e.g., viewing a log file or dashboard) — not permanently lost.
- **SC-004**: 100% of tap-initiated Push-to-Talk recordings show confirm/discard controls and wait for explicit user action; 100% of hold-initiated recordings show only the waveform and complete automatically on release — zero cases of one gesture behaving like the other.
- **SC-005**: 100% of the time, activating Continuous mode presents the dedicated voice view within the same interaction (no extra step), showing exactly two controls.
- **SC-006**: Zero regressions: every acceptance scenario already covered by specs/029-033's test suites unaffected by this feature's scope continues to pass.

## Assumptions

- The dedicated voice view (US3) is a full takeover of the chat panel area — it replaces both the
  text composer (explicitly required by FR-010) and the message history list for the duration the
  view is active, matching how the referenced products' own voice modes work (their text
  transcript is not shown during an active voice session either). History remains fully intact
  and viewable once the user exits (FR-013) — this assumption affects only what's visible *while*
  the view is open, not what's persisted or retrievable afterward.

- Exiting the dedicated voice view (US3) returns the user to the normal chat view without necessarily changing their persisted Push-to-Talk/Continuous mode preference — re-activating Continuous mode later re-enters the same dedicated view, matching how comparable voice-mode UIs in referenced products behave (exiting voice mode doesn't change the assistant's default input mode setting elsewhere).
- The dedicated voice view (US3) reuses the product's existing reactive presence visualization (the particle-sphere component already used elsewhere for Lucy's speaking state) rather than introducing a new visualization, consistent with constitution §7's design-system-reuse principle.
- "Retrievable by an operator after the fact" (FR-003) means production logging is fixed to actually persist somewhere accessible (e.g., enabling the existing hosting layer's stdout capture, or adding a durable log sink) — it does not require building a new admin-facing log viewer UI, which is out of scope.
- No new user permissions, external integrations, or database changes are required.
