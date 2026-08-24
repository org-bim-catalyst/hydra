# Feature Specification: Composer Interaction States Redesign

**Feature Branch**: `039-composer-interaction-states-redesign`

**Created**: 2026-08-24

**Status**: Draft

**Input**: User description: "Implement the composer UI/UX requirements defined in docs/UI-UX-Functional-Requirements.md (read together with its referenced mockup images in docs/images/, which are the authoritative visual reference for exact icon placement, states, and transitions). Feature slug: composer-interaction-states-redesign. Covers: initial composer view; typing state; click-to-talk mode; continuous-conversation mode (idle-listening and typing-while-listening); saved-prompts button removal; composer height control icon swap; per-reply replay/stop control; and a new hold-to-talk mode."

## Clarifications

### Session 2026-08-24

- Q: Today's composer reaches Continuous conversation mode via a small mode-switch icon that flips a *persisted* Settings → Voice preference (`PushToTalk` ↔ `Continuous`), requiring a second, separate click on the mic to actually start listening. The new mockups show the continuous-conversation action (`voiceprint-line`) landing directly in the active-listening view from a single click. Should the redesign (a) keep the persisted preference and existing mode-switch control, only reskinning the icon and still requiring two clicks, (b) drop the persisted preference entirely in favor of two fully independent, stateless buttons, or (c) keep the persisted preference and its Settings page, but collapse "switch mode" + "start listening" into one click? → A: (c) One-click hybrid — keep the existing persisted Settings → Voice mode preference and reuse today's mode-switch control (reskinned from `fingerprint-line` to `voiceprint-line`), but a single click on it both switches the preference to Continuous and immediately starts listening, collapsing today's two-step flow into one action. No changes to Settings UI or backend preference storage are required.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Compose and send a text message (Priority: P1)

A user opens a conversation, sees an inviting empty composer, types a message, and sends it. The composer's controls change appropriately as the user types and after sending, so the interface always shows only the actions that make sense for the current moment.

**Why this priority**: This is the baseline interaction every user performs in every session; if the composer's basic show/hide behavior for its action buttons is wrong, the product feels broken from the first ten seconds of use.

**Independent Test**: Open a conversation, type text, verify the composer's controls swap from voice-entry icons to a send icon, send the message, and verify the composer returns to its empty starting appearance.

**Acceptance Scenarios**:

1. **Given** the user has just opened a conversation, **When** the composer first renders, **Then** it shows an empty text field with an attachment action, a click-to-talk/hold-to-talk voice action, and a continuous-conversation voice action, and no send action is visible.
2. **Given** the composer is empty, **When** the user types the first character, **Then** the two voice actions are replaced by a single send action.
3. **Given** the user has typed a message, **When** the text field is non-empty, **Then** the send action is enabled; **When** the text field is empty, **Then** the send action is disabled.
4. **Given** the user has typed a message and the send action is enabled, **When** the user activates send, **Then** the message is submitted, the text field is cleared, and the composer returns to its empty starting appearance.
5. **Given** the user has typed a message, **When** the user deletes all the typed text without sending, **Then** the composer returns to its empty starting appearance.

---

### User Story 2 - Record and send a voice message via click-to-talk (Priority: P1)

A user prefers speaking over typing. They click the microphone action once, speak, and either confirm to have their speech transcribed into the text field or cancel and discard the recording.

**Why this priority**: Voice input is a core, frequently used entry method for this product; a broken or ambiguous recording flow blocks a primary use case and risks losing captured audio the user believed was saved.

**Independent Test**: From the empty composer, click the microphone action, verify a recording view with distinct confirm/cancel actions appears, and verify each action produces the documented outcome (transcribed text placed in the field and the composer showing its typing-state appearance, or a full return to the empty view with nothing added).

**Acceptance Scenarios**:

1. **Given** the composer is in its empty starting appearance, **When** the user clicks (rather than presses-and-holds) the microphone action, **Then** recording starts and the composer shows a live recording indicator alongside a cancel action and a confirm action, replacing the normal action set.
2. **Given** a click-to-talk recording is in progress, **When** the user activates the cancel action, **Then** the recording is discarded, no text is added, and the composer returns to its empty starting appearance.
3. **Given** a click-to-talk recording is in progress, **When** the user activates the confirm action, **Then** recording stops, the audio is transcribed, and the resulting text is placed in the text field, with the composer transitioning to its typing-state appearance (matching hold-to-talk's confirm behavior in User Story 3).

---

### User Story 3 - Hold-to-talk quick voice capture (Priority: P2)

A user wants the fastest possible way to capture a short voice note without a separate confirm step: press and hold the microphone action, speak, and release to have the transcription land directly in the text field for review before sending.

**Why this priority**: This is a new, faster alternative entry path to the same outcome as User Story 2's confirm step; it improves efficiency for users who already trust the transcription and don't want the extra tap, but the product functions without it, so it ranks behind the primary click-to-talk flow.

**Independent Test**: From the empty composer, press and hold the microphone action, verify the pressed/recording appearance appears with no separate cancel/confirm actions, release, and verify the transcription lands in the text field with the composer now in its typing-state appearance.

**Acceptance Scenarios**:

1. **Given** the composer is in its empty starting appearance, **When** the user presses and holds the microphone action (touch: finger held down; desktop: primary mouse button held down), **Then** recording starts immediately, the microphone action's appearance changes to indicate active recording, and a live recording indicator is shown — with no cancel or confirm actions presented.
2. **Given** a hold-to-talk recording is in progress, **When** the user releases the hold, **Then** recording stops immediately, the audio is transcribed, the transcribed text is placed in the text field, and the composer transitions to its typing-state appearance (send action visible).
3. **Given** the hold-to-talk transcription has populated the text field, **When** the user sends the message or clears the field entirely, **Then** the composer returns to its empty starting appearance, matching the normal typing-state exit behavior.

---

### User Story 4 - Hands-free continuous conversation (Priority: P2)

A user wants to have a spoken back-and-forth with Lucy without repeatedly clicking a microphone action for every turn. They start continuous-conversation mode, speak naturally while the agent keeps listening, optionally mute themselves or type a message mid-conversation, and exit back to normal composing when done.

**Why this priority**: This is a differentiated, higher-effort interaction mode that delivers significant value for hands-free use cases but is used less frequently than basic typing or single-shot voice capture, and depends on those simpler flows already working correctly.

**Independent Test**: From the empty composer, start continuous-conversation mode, verify the agent's avatar and listening state appear, verify mute and exit actions behave as documented, verify typing mid-conversation reveals a send action without exiting the mode, and verify exiting returns to the empty starting appearance.

**Acceptance Scenarios**:

1. **Given** the composer is in its empty starting appearance, **When** the user activates the continuous-conversation action (a single click, reusing today's persisted voice-mode preference control), **Then** the user's voice-mode preference switches to Continuous, listening starts immediately in that same action (no separate second click on the microphone is required), the conversation view displays the agent's circular avatar (shown only during this mode), and the composer shows a listening indicator with a mute/unmute action and an exit action.
2. **Given** continuous-conversation mode is active and unmuted, **When** the user activates the mute action, **Then** the user's audio input is muted and the action reflects the muted state; activating it again unmutes.
3. **Given** continuous-conversation mode is active, **When** the user activates the exit action, **Then** the mode ends, the user's voice-mode preference switches back accordingly, and the composer returns to its empty starting appearance.
4. **Given** continuous-conversation mode is active and idle-listening, **When** the user types in the text field, **Then** the agent continues listening, and the composer reveals a send action instead of removing itself from continuous mode.
5. **Given** continuous-conversation mode is active with typed but unsent text, **When** the user activates send, **Then** the typed message is added to the conversation and the composer returns to the idle-listening continuous-conversation appearance (mode remains active).
6. **Given** continuous-conversation mode is active with typed but unsent text, **When** the user deletes all the typed text, **Then** the composer returns to the idle-listening continuous-conversation appearance (mode remains active).

---

### User Story 5 - Replay a spoken reply (Priority: P3)

A user missed part of what Lucy said, or wants to hear a reply again. They use a small control on the reply itself to play or stop its audio, independent of any other reply.

**Why this priority**: This is a convenience/accessibility enhancement on top of the existing voice-response feature; valuable but not required for the core send/receive loop to function.

**Independent Test**: Send a message that produces a spoken reply, wait for speech to finish, then use the reply's control to start and stop playback, and verify only one reply can play at a time.

**Acceptance Scenarios**:

1. **Given** an assistant reply exists and is not currently speaking or muted, **When** the user views the reply, **Then** a replay action is visible in the reply's lower-right corner showing a "play" appearance.
2. **Given** the assistant is currently speaking a reply (initial playback) or audio is muted, **When** the user views the reply's replay action, **Then** the action is disabled.
3. **Given** a reply's replay action is enabled, **When** the user activates it, **Then** that reply's audio begins playing from the beginning and the action switches to a "stop" appearance.
4. **Given** one reply is currently replaying, **When** the user activates the replay action on a different reply, **Then** the first reply's playback stops before the second reply's playback starts, so at most one reply plays at a time.
5. **Given** a reply is replaying, **When** the user activates the now-"stop" action, **Then** playback stops immediately and the action returns to its "play" appearance.
6. **Given** a reply's playback was stopped partway through, **When** the user activates replay again, **Then** playback restarts from the beginning, not from where it was stopped.

---

### User Story 6 - Composer chrome cleanup (Priority: P3)

A user resizes the composer to fit more or less text, and never sees the deprecated saved-prompts entry point that is being removed from the product.

**Why this priority**: This is a small visual/chrome correction (icon swap plus removal of an unused control) with no behavioral risk to core flows; it improves consistency but nothing else depends on it.

**Independent Test**: Open any composer state and confirm the saved-prompts action is absent everywhere, and use the height controls to confirm they use the updated visual treatment while still expanding/collapsing the composer.

**Acceptance Scenarios**:

1. **Given** any composer state (empty, typing, click-to-talk, hold-to-talk, or continuous-conversation), **When** the user views the composer, **Then** no saved-prompts action is present.
2. **Given** the conversation header's height controls, **When** the user activates the increase-height action, **Then** the composer's height increases, and the action's icon reflects the updated visual treatment.
3. **Given** the conversation header's height controls, **When** the user activates the decrease-height action, **Then** the composer's height decreases, and the action's icon reflects the updated visual treatment.

---

### Edge Cases

- What happens if the user starts a click-to-talk or hold-to-talk recording, then the browser/OS denies or revokes microphone permission mid-recording? The recording session MUST end and surface a visible, user-facing error rather than leaving the composer stuck in a recording appearance (no silent failure).
- What happens if transcription fails or returns empty after a click-to-talk confirm or a hold-to-talk release? The user MUST see a visible error/notice, and the composer MUST return to a usable state (empty or prior typed text preserved) rather than being stuck showing a recording indicator.
- What happens if the user releases a hold-to-talk press almost instantly (e.g., an accidental tap treated as a hold)? A release below the product's existing hold-vs-tap duration threshold MUST be treated as click-to-talk (entering the cancel/confirm review flow described in User Story 2) rather than as a hold-to-talk auto-transcribe — this already matches existing gesture-disambiguation behavior and MUST be preserved.
- What happens if the user switches directly from click-to-talk or hold-to-talk into continuous-conversation mode (e.g., activating the continuous-conversation action while a recording is in progress)? The in-progress recording MUST be resolved (cancelled or completed) before continuous-conversation mode starts, never left running in the background.
- What happens if the user tries to activate replay on a reply while a *different* voice interaction (click-to-talk recording, hold-to-talk recording, or continuous-conversation listening) is active? The product MUST define and enforce a single consistent rule for whether replay is allowed to interrupt/coexist with active recording/listening, and disable the replay action when it is not permitted.
- What happens when the browser tab loses focus or the device screen locks during hold-to-talk (the release event never fires)? The recording MUST have a safeguard so it cannot remain active indefinitely.
- What happens if the user mutes themselves during continuous-conversation mode and then types a message? Muting the microphone MUST NOT block typed input from being composed and sent through the existing typing-while-listening flow.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The composer MUST default to an empty starting appearance on load, showing an attachment action, a microphone action (click-to-talk/hold-to-talk entry point), and a continuous-conversation action, with no send action visible.
- **FR-002**: The composer MUST replace the microphone and continuous-conversation actions with a single send action as soon as the text field contains any user-entered text.
- **FR-003**: The send action MUST be disabled whenever the text field is empty and enabled whenever it contains text.
- **FR-004**: Sending a message MUST clear the text field and return the composer to its empty starting appearance; manually clearing all typed text (without sending) MUST also return the composer to its empty starting appearance.
- **FR-005**: A single click (not a press-and-hold) on the microphone action, from the empty composer appearance, MUST start click-to-talk recording and replace the normal action set with a live recording indicator plus a distinct cancel action and confirm action. The microphone action is not reachable once the composer is in its typing-state appearance (FR-002 already removes it from view at that point).
- **FR-006**: Activating the click-to-talk cancel action MUST discard the recording without adding any text and return the composer to its empty starting appearance.
- **FR-007**: Activating the click-to-talk confirm action MUST stop recording, submit the audio for transcription, place the resulting text into the text field, and transition the composer to its typing-state appearance (matching hold-to-talk's FR-010).
- **FR-008**: A press-and-hold on the microphone action (pointer/touch held down), from the empty composer appearance, MUST start hold-to-talk recording immediately, without requiring a separate confirm step to begin.
- **FR-009**: While a hold-to-talk recording is active, the composer MUST show a distinct "actively recording" appearance for the microphone action and a live recording indicator, and MUST NOT present the click-to-talk cancel/confirm actions.
- **FR-010**: Releasing the hold-to-talk press MUST immediately stop recording, submit the audio for transcription, place the resulting transcribed text into the text field, and transition the composer to its typing-state appearance.
- **FR-011**: After a hold-to-talk transcription populates the text field, the composer MUST follow the same send/clear rules as any other typed text (FR-004).
- **FR-012**: Activating the continuous-conversation action from the empty composer appearance MUST, in a single action, switch the user's persisted voice-mode preference to Continuous and start listening immediately (no separate second action on the microphone required to begin listening), display the agent's circular avatar in the conversation view (shown only while this mode is active), and show a listening indicator with a mute/unmute action and an exit action. This action reuses today's existing persisted voice-mode preference and its control (visually replacing that control's current icon), not a new independent, stateless button — no new preference storage is introduced.
- **FR-013**: The mute/unmute action in continuous-conversation mode MUST toggle whether the user's audio input is captured, and its appearance MUST reflect the current muted/unmuted state.
- **FR-014**: The exit action in continuous-conversation mode MUST end the mode, switch the user's persisted voice-mode preference back accordingly, and return the composer to its empty starting appearance.
- **FR-015**: While continuous-conversation mode is active and idle-listening, typing in the text field MUST reveal a send action without ending continuous-conversation mode.
- **FR-016**: Sending a typed message while continuous-conversation mode is active MUST add the message to the conversation and return the composer to the idle-listening continuous-conversation appearance, without ending the mode.
- **FR-017**: Clearing all typed text while continuous-conversation mode is active MUST also return the composer to the idle-listening continuous-conversation appearance, without ending the mode.
- **FR-018**: The composer MUST NOT display a saved-prompts action in any state or mode (empty, typing, click-to-talk, hold-to-talk, or continuous-conversation).
- **FR-019**: The composer height controls MUST use an updated visual treatment for increase-height and decrease-height actions while preserving their existing expand/collapse behavior.
- **FR-020**: Every assistant reply MUST display a replay action in its lower-right corner when audio is not muted and no reply is currently speaking or replaying.
- **FR-021**: The replay action for a given reply MUST be disabled while the assistant is currently speaking that reply for the first time, or while audio is muted.
- **FR-022**: Activating an enabled replay action MUST start that reply's audio playback from the beginning and change the action's appearance to a stop control.
- **FR-023**: At most one reply's audio (spoken response or replay) MUST be playing at any given time; starting playback/replay on one reply MUST stop any other reply currently playing.
- **FR-024**: Activating the stop control on a currently replaying reply MUST stop playback immediately and return the action to its playable appearance.
- **FR-025**: Restarting replay on a reply after it was stopped MUST always begin playback from the beginning, never resume from the stopped position.
- **FR-026**: Any failure in recording, transcription, or playback (permission denial, transcription error, empty/failed audio) MUST surface a visible, user-facing indication and return the composer or reply control to a defined, non-stuck state — no interaction may fail silently.

### Key Entities

- **Composer State**: The current interaction mode of the message-entry area (empty, typing, click-to-talk recording, hold-to-talk recording, continuous-conversation idle-listening, continuous-conversation typing) and the text content it currently holds.
- **Voice Recording Session**: A single instance of user audio capture (click-to-talk or hold-to-talk), tracked from start to its resolution (cancelled, confirmed/transcribed, or failed).
- **Continuous Conversation Session**: The active/inactive state of hands-free mode, including whether the user's microphone is muted and whether the agent is currently listening or the user is composing typed text within it.
- **Assistant Reply Playback**: The play/stop/disabled state of a given assistant reply's spoken audio, including which single reply (if any) is currently playing across the conversation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can complete a full send-a-message cycle (type, send, composer resets) with zero visible layout glitches or incorrect action visibility, verified across 100% of the defined composer states.
- **SC-002**: Users can complete a click-to-talk voice capture (start, speak, confirm) and see transcribed text appended to the composer without needing to retry, in at least 95% of attempts under normal microphone/network conditions.
- **SC-003**: Users can complete a hold-to-talk voice capture (press, speak, release) at least as quickly as the equivalent click-to-talk flow, measured as fewer or equal discrete user actions (press+release vs. click+click).
- **SC-004**: Zero instances of the saved-prompts action appearing in any composer state, verified across all defined states.
- **SC-005**: Users can start, mute/unmute, type within, and exit continuous-conversation mode, returning to the empty composer appearance 100% of the time when the exit action is used.
- **SC-006**: Users can never have two assistant replies playing audio simultaneously, verified across repeated replay attempts on multiple replies in the same conversation.
- **SC-007**: Every recording, transcription, or playback failure produces a user-visible message within the same interaction — zero silent failures observed in testing of the documented edge cases.

## Assumptions

- The visual mockups in `docs/images/` (figure-image-1.png through figure-image-11.png) are treated as the authoritative reference for exact icon choice, placement, and per-state layout; this specification describes the required states and transitions in product terms without re-specifying pixel-level layout, which the implementation phase will derive directly from those images.
- This feature is scoped to the composer's interaction states and the per-reply replay control; it does not introduce new backend transcription, text-to-speech, or AI-provider capabilities beyond what existing voice/transcription functionality already provides — it changes when/how existing capabilities are surfaced and which controls are shown.
- "Continuous-conversation mode" and "click-to-talk" already exist in the product prior to this feature; this specification's continuous-conversation and click-to-talk requirements describe the target behavior (including any corrections to current behavior implied by the requirements doc), not net-new capability from zero. In particular, click-to-talk's tap-to-record-with-confirm/cancel behavior and hold-to-talk's press-and-release-to-transcribe behavior are already implemented today as two gestures on the same microphone control (distinguished by hold duration); this feature's work for those two flows is primarily about the surrounding entry-point/visual redesign, not building the gesture logic from scratch.
- Hold-to-talk is a new *name* for an entry path that already exists in the product's gesture handling (see above) alongside click-to-talk; both remain available side by side (a click starts click-to-talk, a press-and-hold starts hold-to-talk) rather than one replacing the other.
- The continuous-conversation action reuses the product's existing persisted voice-mode preference (`PushToTalk` ↔ `Continuous`, set via Settings → Voice) and its existing mode-switch control, rather than introducing a new independent, stateless button or new stored state — see Clarifications. Icon sizing/visual weight for this control follows the mockup images (per the assumption below on images as the authoritative visual reference), even though its underlying control is reused rather than newly built.
- Desktop pointer interaction for hold-to-talk is scoped to the primary (left) mouse button; touch interaction is scoped to a single-finger press-and-hold. Other pointer types (pen, secondary buttons) are out of scope.
- The rule governing whether the replay action may interrupt or coexist with an active voice-recording/listening session (flagged in Edge Cases) defaults to: replay is disabled while any voice-recording or continuous-listening session is active, since both consume the same audio subsystem; this may be revisited during planning if it conflicts with existing voice-engine behavior.
- Accessibility, keyboard-operability, and responsive-layout requirements follow the project's existing UI principles (constitution §7) and are not re-stated as feature-specific requirements here.
