# Feature Specification: Voice Controls & Composer Redesign

**Feature Branch**: `031-voice-controls-redesign`

**Created**: 2026-08-20

**Status**: Draft

**Input**: User description: "Redesign the Ask Lucy chat composer's voice controls and attachment/translate behavior based on live user-acceptance testing of the just-shipped specs/030-composer-panel-refinements composer/panel layout: (1) confirm attach-file already supports PDF extraction, audio transcription, and CSV, and fix any real defect in how that reads to the user; (2) remove the translate feature entirely; (3) redesign voice controls into mode-specific views (Continuous vs Push-to-Talk) instead of one crowded shared row, following ChatGPT/Claude's pattern; (4) preserve Continuous mode's existing mic-mutes-microphone behavior; (5) fix the Push-to-Talk flow so a tap-then-accept or a press-and-hold both end by inserting transcribed text into the message field as editable draft text, with exactly one Send action afterward (removing today's confusing extra 'send to transcribe' button); (6) relocate the mute/unmute-Lucy speaker control from the composer footer into the panel header, next to Lucy's portrait/name."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Push-to-Talk recording reliably becomes editable draft text (Priority: P1)

A user in Push-to-Talk mode taps the mic to start recording, speaks, then taps Finish to signal they're done. Today, tapping Finish only moves to a second review step showing a confusing extra "send to transcribe" control (visually similar to the real Send button) instead of the transcription simply landing in the text field — the user doesn't know what to click next, and in testing the flow surfaced a transcription failure with no clear recovery path. The user wants exactly what ChatGPT and Claude do: recording produces editable text in the message field the moment they signal they're done, and the existing Send button (already in the composer footer) is the only remaining step to actually send it.

**Why this priority**: This is the most broken, most confusing interaction reported — a core voice-input path that currently produces an extra, unexplained control instead of the expected result. It blocks trust in voice input entirely until fixed.

**Independent Test**: In Push-to-Talk mode, tap the mic once, speak, tap Finish. Verify no additional "send to transcribe" control ever appears — the transcribed text appears directly in the message field, editable, with the composer's normal Send button as the only next action.

**Acceptance Scenarios**:

1. **Given** Push-to-Talk mode with an idle mic, **When** the user taps the mic once (a quick tap, not a hold), **Then** recording starts and Finish/Cancel controls appear.
2. **Given** an active tap-started Push-to-Talk recording, **When** the user taps Finish, **Then** the recording is transcribed and the resulting text is inserted into the message text field as editable draft content — no other control appears as an intermediate step.
3. **Given** transcribed text now sitting in the message field, **When** the user reviews/edits it and taps the composer's existing Send button, **Then** the message is sent to Lucy — this is the only send action available at any point in this flow.
4. **Given** an active Push-to-Talk recording, **When** the user taps Cancel instead of Finish, **Then** the recording is discarded and the message field is left unchanged (not populated with any transcription).

---

### User Story 2 - Press-and-hold (hold-to-talk) completes automatically on release (Priority: P1)

A user in Push-to-Talk mode presses and holds the mic button, speaks while holding, and releases. They expect the hold gesture itself to be the complete interaction — release finishes the recording and produces editable draft text immediately, with no separate Finish press required (the explicit Finish tap is only needed for the tap-to-start path; release itself is the equivalent signal for a hold).

**Why this priority**: Equally core to the Push-to-Talk experience as User Story 1, and shares the same underlying "recording → transcribed draft text" outcome — bundling them as the MVP for Push-to-Talk correctness.

**Independent Test**: In Push-to-Talk mode, press and hold the mic, speak, then release. Verify the recording stops the instant the button is released and the transcribed text appears in the message field without any further tap.

**Acceptance Scenarios**:

1. **Given** Push-to-Talk mode with an idle mic, **When** the user presses and holds the mic button for longer than a brief instant while speaking, **Then** recording continues only for as long as the button remains held.
2. **Given** an in-progress held recording, **When** the user releases the button, **Then** recording stops immediately and the audio is transcribed into the message text field as editable draft content, with no additional tap required for this path.
3. **Given** a hold-to-talk recording has just populated the text field, **When** the user taps Send, **Then** the message sends — identical end state to the tap-then-finish path in User Story 1.

---

### User Story 3 - Mode-specific voice control views (Priority: P2)

A user switching between Continuous and Push-to-Talk conversation modes sees a voice-control presentation tailored to that mode. The most crowded moment today is a Push-to-Talk recording in progress, where the mic/mode-switch/preferences-warning controls stay visible alongside the recording waveform and Finish/Cancel buttons all at once — mirroring how ChatGPT and Claude instead show only recording-relevant controls (waveform, stop, cancel) while actively recording, and restore the normal toolbar once it ends.

**Why this priority**: A significant clarity/usability improvement flagged directly from user testing ("overwhelming," "confusing"), but it is a presentation-layer change on top of the flows already corrected in User Stories 1-2, so it follows them.

**Independent Test**: Switch conversation mode between Continuous and Push-to-Talk with the composer idle (not recording). Verify each mode shows a distinctly simpler, mode-appropriate set of voice controls rather than one shared row with every mode's controls visible at once.

**Acceptance Scenarios**:

1. **Given** Continuous mode is active and idle, **When** the user views the composer, **Then** the voice-control area shows only what Continuous mode needs (the mic mute/unmute toggle) — not Push-to-Talk-only affordances like a hold gesture hint.
2. **Given** Push-to-Talk mode is active and idle, **When** the user views the composer, **Then** the voice-control area shows only what Push-to-Talk mode needs (a mic control that supports both tap-to-record and hold-to-record) — not Continuous-only affordances.
3. **Given** Push-to-Talk mode, **When** a recording is actively in progress, **Then** the recording-specific controls (waveform, Finish, Cancel) replace the idle mic control, and other footer controls not relevant to an in-progress recording (attach, insert-prompt, mode-switch) are hidden until the recording ends.
4. **Given** the user switches modes via the existing mode-switch menu, **When** the switch completes, **Then** the voice-control view updates to the new mode's presentation without requiring a page reload or losing any in-progress typed text.

---

### User Story 4 - Continuous mode's mic behavior is preserved (Priority: P2)

A user in Continuous mode taps the mic icon to mute or unmute their own microphone (start/stop the assistant listening) — this already works correctly today and must not regress while the surrounding presentation is redesigned in User Story 3.

**Why this priority**: Explicitly called out by the user as already-correct behavior that must survive the redesign — protecting it is a direct acceptance criterion of User Story 3's changes, tracked separately so a regression here is caught independently.

**Independent Test**: In Continuous mode, tap the mic once to start listening, confirm the assistant is listening; tap again to stop. Verify this exact toggle behavior is unchanged after the User Story 3 redesign ships.

**Acceptance Scenarios**:

1. **Given** Continuous mode with the mic off, **When** the user taps the mic icon, **Then** the microphone starts listening (matching the platform's existing Continuous-mode capture behavior).
2. **Given** Continuous mode with the mic actively listening, **When** the user taps the mic icon again, **Then** listening stops.

---

### User Story 5 - Translate feature removed (Priority: P3)

A user no longer sees a translate control anywhere in the chat composer — the feature has been deliberately discontinued for this surface, not merely hidden or relocated.

**Why this priority**: A clear, low-risk deletion with no dependent behavior to coordinate — lowest priority since it's independent of every other change in this feature and carries no interaction-flow risk.

**Independent Test**: Open the chat composer in any conversation mode and state (idle, recording, with or without a prior assistant response). Verify no translate button, icon, or menu item appears anywhere in the composer or its footer.

**Acceptance Scenarios**:

1. **Given** any composer state, **When** the user looks at the footer controls, **Then** no translate button is present.
2. **Given** a conversation with at least one assistant response, **When** the user reviews available actions on that response and in the composer, **Then** no translate action is reachable from the chat widget.

---

### User Story 6 - Mute/unmute Lucy moves to the panel header (Priority: P3)

A user wants to mute or unmute Lucy's spoken responses using a control that reads as part of Lucy's own identity/status area (next to her portrait and name) rather than as one of the message-composition tools in the footer, matching the mental model that muting is "muting Lucy," not "a message-typing action."

**Why this priority**: A placement/relocation change with no new behavior (the merged mute+stop-speaking behavior from specs/029-fix-chat-widget-bugs is unchanged) — self-contained and low-risk, so it lands last.

**Independent Test**: Open the expanded chat panel. Verify the mute/unmute-Lucy control appears in the header immediately next to Lucy's portrait/name/status, and is no longer present in the composer footer. Verify muting/unmuting behaves exactly as before (including stopping in-progress speech when muted).

**Acceptance Scenarios**:

1. **Given** the expanded chat panel is open, **When** the user looks at the header, **Then** a mute/unmute-Lucy control appears immediately to the right of Lucy's portrait/name/status block.
2. **Given** the composer footer, **When** the user looks for a mute/unmute-Lucy control there, **Then** none is present — it has moved, not duplicated.
3. **Given** Lucy is actively speaking a response, **When** the user activates the relocated mute control, **Then** the in-progress speech stops immediately and further responses remain silent (text-only) until unmuted, identical to the pre-relocation merged behavior.

---

### Edge Cases

- What happens if the user releases a hold-to-talk gesture almost instantly? Anything that doesn't clear the existing tap-vs-hold gesture threshold is already classified as a tap (not a hold) by that existing distinction, so it never reaches this case; anything that does clear the threshold is treated as a deliberate hold and sent for transcription like any other hold, even if brief (see FR-014 and Assumptions — no new, separate "too short to transcribe" duration check is introduced).
- What happens if the user wants to abort a hold-to-talk recording while still holding the button, before releasing? The existing Cancel control remains available and discards the recording exactly as it does for a tap-started recording — no new gesture (e.g. a drag-away/release-outside-bounds) is introduced; releasing normally (without cancelling first) always finalizes and transcribes.
- What happens if transcription fails (e.g., a backend error) after a tap-started recording finishes or a hold is released? The user must see a clear, visible error (not a silent failure) and must not be left staring at an unexplained extra button — the message field's existing content (if any) must remain untouched, and the user must be able to retry recording.
- What happens if the user switches conversation mode while a recording is in progress? Mode-switching remains blocked for the entire duration of an active Push-to-Talk recording session (recording or transcribing) — extending, not replacing, the existing mode-switch-disabled-while-listening behavior — so a recording is never interrupted or left in an inconsistent state by a mode change.
- What happens to any text the user had already typed in the message field before starting a Push-to-Talk recording? Finishing a recording must not silently overwrite that existing draft text without the user's awareness — clarified in Assumptions below.
- What happens when the user attaches a PDF, an audio file, or a CSV through the paperclip control after this change? All three must continue to work exactly as they do today (this feature does not touch that logic beyond investigating the reported "audio only" perception).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST insert transcribed text from a tap-started Push-to-Talk recording directly into the message text field as editable draft content the moment the user signals they're finished (tapping Finish, or tapping the mic again to stop) — with no additional confirmation or "send to transcribe" step between that signal and the field being populated.
- **FR-002**: The system MUST insert transcribed text from a completed hold-to-talk (press-and-release) Push-to-Talk recording directly into the message text field as editable draft content immediately upon release, using the same immediate transcribe-and-populate behavior as FR-001 — with no separate confirmation step for this gesture either.
- **FR-003**: The composer's existing Send action MUST be the only mechanism to send a message after either Push-to-Talk transcription path completes — no other send-like control may appear as part of the recording/transcription flow.
- **FR-004**: The system MUST allow the user to cancel a Push-to-Talk recording at any point while it is actively recording (whether started by a tap or a hold) via the existing Cancel control, such that the message text field is left unchanged.
- **FR-005**: The system MUST distinguish a genuine hold gesture from a quick tap on the Push-to-Talk mic control (preserving the existing hold-vs-tap timing distinction), routing holds to the hold-to-talk flow (FR-002) and taps to the tap-then-finish flow (FR-001/FR-004).
- **FR-006**: The voice-control presentation MUST differ by conversation mode: Continuous mode's idle view MUST show only the mic mute/unmute control; Push-to-Talk mode's idle view MUST show only the tap-or-hold mic control — neither mode's idle view may show controls exclusive to the other mode.
- **FR-007**: In Continuous mode, the mic control MUST start/stop the microphone (mute/unmute listening) via a single tap, unchanged from current behavior.
- **FR-008**: While a Push-to-Talk recording is actively in progress (tap-started or hold-started), the recording-specific controls (live waveform, Finish, Cancel) MUST replace the idle mic control and MUST hide other footer controls not relevant to an in-progress recording (e.g. attach, insert-prompt, mode-switch) — restoring them once the recording ends (transcribed or cancelled).
- **FR-009**: Switching conversation mode MUST update the voice-control presentation to the newly selected mode without a page reload and without discarding any text already typed in the message field.
- **FR-010**: The translate button and its underlying click behavior MUST be removed entirely from the chat composer and MUST NOT be reachable from any other control in the chat widget.
- **FR-011**: The mute/unmute-Lucy control MUST be relocated from the composer footer to the expanded panel's header, positioned immediately adjacent to Lucy's portrait/name/status block.
- **FR-012**: The mute/unmute-Lucy control's behavior (including stopping in-progress speech immediately when muted, per specs/029-fix-chat-widget-bugs) MUST be unchanged by the relocation in FR-011.
- **FR-013**: The attach-file control MUST continue to support PDF text extraction, audio transcription, and CSV reading exactly as before this feature; any genuine defect found in how the control communicates its supported formats to the user MUST be fixed, without expanding format support beyond these three.
- **FR-014**: A gesture that does not clear the existing tap-vs-hold timing threshold MUST continue to be classified as a tap (per FR-005), not a hold — this is the sole mechanism by which a near-instant press-and-release is prevented from being treated as a completed hold-to-talk recording; no separate, additional minimum-recording-duration check is required.
- **FR-015**: A transcription failure (e.g., a backend error) MUST be surfaced to the user as a visible, specific error — never a silent failure — and MUST leave any pre-existing message field content untouched, with the user able to attempt the recording again.

### Key Entities

*(Not applicable — this feature changes UI presentation and interaction flow for existing voice/attachment/mute state; it introduces no new data entities.)*

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of finished Push-to-Talk recordings (tap-then-finish or hold-and-release) result in transcribed text appearing directly in the message field, with zero occurrences of an intermediate "send to transcribe" control.
- **SC-002**: Users can go from "start recording" to "message sent" in Push-to-Talk mode using exactly two distinct user actions for the tap path (tap mic to start, tap Finish, tap Send — three taps total, none of which is a redundant transcription-confirmation step) or the hold path (hold-and-speak, release, tap Send — two actions total).
- **SC-003**: In a side-by-side comparison, each conversation mode's idle voice-control view shows no controls belonging exclusively to the other mode — zero cross-mode control leakage.
- **SC-004**: Zero occurrences of a translate control anywhere in the chat composer or its footer after this feature ships.
- **SC-005**: 100% of users opening the expanded chat panel can locate the mute/unmute-Lucy control next to Lucy's portrait/name within the header, with none remaining in the composer footer.
- **SC-006**: Zero regressions: every acceptance scenario already covered by specs/029-fix-chat-widget-bugs's and specs/030-composer-panel-refinements's voice/composer/panel test suites (excluding the translate-specific tests removed by User Story 5) continues to pass unchanged after this feature ships.

## Assumptions

- If the user has existing typed text in the message field when a Push-to-Talk recording finishes (tap-then-finish or hold-and-release), the transcribed text is appended to that existing content (not silently replacing it) — consistent with the current `handleFile`/`transcribeAudio` pattern in the codebase, which already appends rather than overwrites.
- Resolved without a blocking clarification (informed default, per spec-kit's "no reasonable default exists" bar not being met here): FR-014's "too short to represent a real utterance" is fully satisfied by reusing the *existing* hold-vs-tap timing threshold already implemented in the composer (research.md/FR-005 of specs/029-fix-chat-widget-bugs) — no new, separate minimum-recording-duration check is introduced. Any hold that clears that threshold is sent for transcription like any other hold, even if brief; a near-empty/nonsensical transcription result is handled by the existing empty-result/error path, not a new discard rule.
- Resolved the same way: no new mid-hold cancel *gesture* (e.g. "release outside the button" or "swipe away") is introduced. The existing Cancel control remains available and functional throughout any active recording, tap-started or hold-started alike — this is already-existing, already-tested behavior, not new logic, so a user holding the mic can still tap Cancel (with a second input, e.g. keyboard) to discard before releasing if needed. What's new is only that *releasing normally* (without cancelling) now finalizes and transcribes immediately, with no separate confirmation step in between.
- The "attach only supports audio" perception (FR-013) is scoped to investigating and fixing how the existing multi-format support is surfaced/communicated (e.g., file-picker behavior, any misleading label or icon) — this feature does not add support for file types beyond PDF, audio, and CSV, and does not fix the reported backend "Transcription failed with 500" error, which is a separate operational/backend concern outside this specification's scope.
- Removing the translate feature (User Story 5) is a deliberate, explicit product decision confirmed directly by the user, intentionally superseding CLAUDE.md's general "Translate content" platform-vision bullet for this specific chat-widget control only; it does not imply translation is removed from the broader platform vision.
- The underlying voice/recording/mute state management (`useSpeechRecognition`, `useVoiceRecorder`, `useVoiceOutput`, the merged mute+stop-speaking behavior) is unchanged by this feature — only which UI surface exposes each control, and how the Push-to-Talk transcription result reaches the message field, change.
- No new user permissions, data, or backend changes are required — this is a client-side interaction-flow and layout feature only, aside from the investigation named in FR-013.
