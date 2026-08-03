# Feature Specification: Restore Voice Output Mute & Input Mode Controls

**Feature Branch**: `013-restore-voice-controls`

**Created**: 2026-08-03

**Status**: Draft

**Input**: User description: "Restore voice UI controls that were previously implemented but are missing from the current build: (1) a mute button for TTS (text-to-speech) output, allowing the user to mute/unmute Lucy's spoken responses; (2) a way to switch STT (speech-to-text) input mode between 'push-to-talk' (hold to speak) and 'continuous listening' (always-on mic that listens continuously). These were previously implemented but have regressed / are not present in the latest implementation and need to be brought back with a proper spec."

## Clarifications

### Session 2026-08-03

- Q: How should push-to-talk activation work? → A: Both hold and toggle supported — press-and-hold is primary, with a click/press-to-toggle fallback for users who can't hold.
- Q: When the user unmutes mid-reply, should the reply that was muted resume from where it stopped, or only future replies get spoken? → A: Only future replies speak; the reply that was mid-playback when muted is not resumed or replayed.
- Q: If continuous listening is on and the user starts typing in the message box, should the microphone keep listening? → A: Keep listening regardless — spoken and typed input are independent and either can be sent.
- Q: If the user switches from push-to-talk to continuous listening while mid-hold (actively capturing), what should happen to that in-progress capture? → A: Block the switch until released — the mode toggle is disabled while a push-to-talk capture is active.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Mute Lucy's spoken responses (Priority: P1)

While Lucy is speaking a reply aloud (or about to), a user wants to silence the audio without interrupting the conversation itself — for example because someone just walked into the room, or they'd rather read the reply than hear it.

**Why this priority**: This is the most frequently needed control any time voice output is on by default, and its absence is the most disruptive gap — right now there is no way to stop Lucy talking except leaving the page or turning down system volume. It is also the smallest, most self-contained piece of restored functionality, making it the fastest path back to a usable voice experience.

**Independent Test**: Send a message that triggers a spoken reply, mute audio output while Lucy is speaking, and confirm the audio stops immediately while the reply still finishes generating and appears as text. Can be fully tested without touching the microphone/input side of voice at all.

**Acceptance Scenarios**:

1. **Given** Lucy is speaking a reply aloud, **When** the user selects mute, **Then** audio output stops immediately, but reply generation continues uninterrupted and the reply text keeps streaming in as normal.
2. **Given** audio output is muted, **When** a new reply is generated, **Then** it is not spoken aloud, and no audio queues up to play once the user unmutes.
3. **Given** audio output is muted, **When** the user selects unmute, **Then** subsequent replies are spoken aloud again; the reply(s) that were generated while muted are not played back retroactively.
4. **Given** the voice output mute control, **When** a user operates it using only the keyboard, **Then** it can be discovered, focused, and activated without a pointing device.
5. **Given** a user muted audio output in a previous session, **When** they return in a new session, **Then** audio output remains muted until they explicitly unmute it.

---

### User Story 2 - Choose how the microphone listens (Priority: P2)

A user wants to control how their spoken input is captured: either by holding down a control while they talk ("push-to-talk"), or by leaving the microphone continuously listening so they can speak hands-free without pressing anything each time ("continuous listening").

**Why this priority**: This restores hands-free operation, which matters for accessibility and for users who want a natural back-and-forth voice conversation, but it is more involved than the mute control (it changes how input capture behaves, not just whether output is audible) and most users can still get value from voice mode with push-to-talk alone in the meantime.

**Independent Test**: Switch input mode to continuous listening, speak without pressing anything, and confirm speech is captured and sent; switch back to push-to-talk and confirm the microphone only captures while explicitly held/activated. Can be tested independently of the mute control in User Story 1.

**Acceptance Scenarios**:

1. **Given** the input mode is set to push-to-talk, **When** the user is not actively holding/activating the microphone control, **Then** the microphone is not capturing audio.
2. **Given** the input mode is set to push-to-talk, **When** the user either holds down the control while speaking or presses it once to start and again to stop, **Then** the spoken input is captured for the duration and, once released/stopped, processed as input.
3. **Given** the input mode is set to continuous listening, **When** the user speaks without touching any control, **Then** their speech is captured and processed as input once they pause, and the microphone remains ready to listen for the next thing they say.
4. **Given** an ongoing conversation, **When** the user switches between push-to-talk and continuous listening, **Then** the switch takes effect immediately for the next input without restarting the conversation or losing conversation history.
5. **Given** the user has not granted microphone access, **When** they select continuous listening, **Then** they are prompted for microphone permission and shown a clear, actionable message if access is denied.
6. **Given** a user selected continuous listening in a previous session, **When** they return in a new session, **Then** their input mode preference is restored automatically (microphone activity itself still requires the normal permission/activation flow — it is the mode choice that is remembered, not an active mic).

---

### Edge Cases

- Muting and then quickly unmuting while Lucy is mid-sentence does not resume or replay the interrupted reply — only replies generated after unmuting are spoken (see Clarifications).
- Continuous listening stays active even while the user types in the text composer; spoken and typed input are independent, and either can be sent (see Clarifications).
- Attempting to switch from push-to-talk to continuous listening while a push-to-talk capture is actively in progress is blocked until the user releases/stops that capture (see Clarifications).
- What happens if microphone or audio-output hardware is disconnected while continuous listening or unmuted playback is active?
- How does the system indicate, at a glance, whether output is currently muted and which input mode is active, so the user isn't left guessing why nothing is being heard or captured? *(Addressed by FR-008.)*

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a persistently visible control that lets the user mute and unmute Lucy's spoken (audio) responses.
- **FR-002**: Muting audio output MUST NOT interrupt, cancel, or delay generation of the AI's reply — only the audible playback is suppressed.
- **FR-003**: While muted, replies generated by the AI MUST NOT be queued for playback and MUST NOT play automatically upon unmuting; a reply that was already mid-playback at the moment of muting is not resumed or replayed after unmuting — only replies generated after the unmute take effect.
- **FR-004**: The system MUST provide a control that lets the user choose the microphone's input mode: push-to-talk or continuous listening.
- **FR-005**: In push-to-talk mode, the microphone MUST only capture audio while the user is explicitly holding the control (or a bound key) down, or between one activating press and one deactivating press for users who use press-to-toggle instead of holding; capture MUST stop as soon as the control is released or the deactivating press occurs.
- **FR-006**: In continuous listening mode, the microphone MUST remain active and capture spoken input without requiring the user to press or hold any control for each utterance, until the user turns off continuous listening or the mode is switched; this remains true even while the user is simultaneously typing in the text composer — spoken and typed input are independent and either can be sent.
- **FR-007**: Users MUST be able to switch between push-to-talk and continuous listening at any time during an active conversation without restarting the conversation or losing conversation history, EXCEPT that switching away from push-to-talk MUST be blocked while a push-to-talk capture is actively in progress, until the user releases/stops that capture.
- **FR-008**: The system MUST visibly indicate the current mute state and the current input mode at all times voice controls are available, so the user can tell at a glance why audio is or isn't playing/listening.
- **FR-009**: The system MUST request microphone permission when continuous listening or push-to-talk is first used, and MUST show a clear, actionable message if permission is denied or unavailable.
- **FR-010**: The mute control and the input-mode control MUST both be operable via keyboard alone, in addition to pointer/touch interaction.
- **FR-011**: The system MUST persist each user's mute state and input-mode preference and restore them automatically the next time that user returns, without requiring reconfiguration.
- **FR-012**: If voice output or voice input becomes unavailable (e.g., permission denied, hardware/service failure), the system MUST surface a visible, specific message rather than silently doing nothing.

### Key Entities

- **Voice Output Preference**: Per-user setting recording whether spoken AI output is currently muted.
- **Voice Input Mode Preference**: Per-user setting recording the selected microphone mode (push-to-talk or continuous listening).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can silence an in-progress spoken reply within 1 second of choosing to mute, every time.
- **SC-002**: 100% of replies generated while output is muted are never audibly played back after the fact, verified across repeated mute/unmute cycles.
- **SC-003**: A user can switch between push-to-talk and continuous listening in a single action, with the new mode active for the very next spoken input.
- **SC-004**: A returning user's mute state and input-mode choice match what they last set, in 100% of returning sessions, without any manual reconfiguration.
- **SC-005**: Both voice controls (mute, input mode) are operable start-to-finish using only a keyboard, with no loss of functionality compared to pointer use.
- **SC-006**: Users experiencing a microphone-permission or voice-service failure are shown a specific, actionable on-screen message within 2 seconds — never a silent failure with no visible outcome.

## Assumptions

- These two controls existed in a prior implementation of the voice experience and are being restored, not designed from scratch; where this spec's decisions (mute suppresses only playback, mode switch is instant, preferences persist per user, all controls are keyboard-operable) coincide with that prior design, that is intentional continuity rather than coincidence.
- "Continuous listening" means the microphone keeps listening for successive utterances without per-utterance activation, not that a single utterance can be arbitrarily long — normal pause-based end-of-speech detection still applies.
- Mute and input-mode preferences are scoped per authenticated user (consistent with this platform's existing long-term voice/preference persistence), not per device or per conversation.
- Voice output and voice input continue to rely on whatever underlying speech provider(s) are already integrated; this spec covers the user-facing mute and mode controls around that output/input, not the provider integration itself.
- Both press-and-hold and press-to-toggle activation are supported for push-to-talk across pointer, touch, and keyboard input, per Clarifications; the interaction otherwise follows the platform's existing conventions for press-and-hold controls.
