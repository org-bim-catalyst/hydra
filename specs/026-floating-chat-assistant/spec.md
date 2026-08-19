# Feature Specification: Floating Chat Assistant Redesign

**Feature Branch**: `026-floating-chat-assistant`

**Created**: 2026-08-17

**Status**: Draft

**Input**: User description: "Remove the \"+ new chat button\" as every new session should start new chat by default. Remove the drop down menu of the language, as the user should set the default language from the chat configuration, only show a flag of the current active language in circle and to be updated when the user change it from the settings. Remove the \"Generate image\" button as this will be done by agent later from the conversation directly. When the user press the record button the UI should have the same ChatGPT look and feel, a button to show the user has finished speaking, a button to cancel the recording and go back to typing mode and a button to send the recording for transcription after the user accepts, previously implemented showing a waveform during the recording of the user speech. Redesign the chat interface as a floating AI assistant integrated into the Flumeria urban design workspace, modeled on the supplied readdy.ai preview and reference screenshots, with two primary states — Collapsed (a narrow floating vertical control with expand handle, real-time vertical voice analyzer, Push-to-Talk, Continuous Listening toggle, Mute Agent control, and a minimal status indicator communicating Idle/Processing/Speaking) and Expanded (full conversation view with the analyzer hidden, messages, chat input, and voice controls). The chat must remain above the viewer without changing its layout, support smooth animated transitions between states, and work with mouse, keyboard, and touch. Do not redesign the underlying AI/LLM functionality — preserve existing chat/agent behavior and focus on the UI/UX and interaction model."

## Clarifications

### Session 2026-08-17

- Q: Now that "+ New chat" is removed from the widget entirely, is there still any way for a user to deliberately start a brand-new conversation mid-session (without reloading the page)? → A: Yes — a minimal, icon-only affordance remains in the Expanded state for starting a new conversation without reloading; it is not the previous prominent, text-labeled "+ New chat" button.
- Q: How should the voice-recording review flow (waveform → finished-speaking → cancel/send) work relative to today's live speech recognition? → A: Discrete record-then-transcribe — audio is buffered client-side while recording (waveform only, no live partial transcript); nothing is sent to the transcription endpoint until the user explicitly accepts.
- Q: Does the finished-speaking/cancel/send review flow apply to Continuous Listening mode too, or only to Push-to-Talk? → A: Push-to-Talk only; Continuous Listening keeps its current always-on, no-confirmation hands-free behavior unchanged.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Arriving to an unobstructed, collapsed assistant (Priority: P1)

A Studio user loads the workspace and, instead of a full chat panel or a bare circular icon, sees a narrow floating vertical control hugging one edge of the screen: an expand handle, a live voice analyzer, and voice controls — nothing that covers the design viewer underneath.

**Why this priority**: This is the default, most-seen state of the feature and the foundation the Expanded state opens from. If the collapsed control isn't lightweight and unobtrusive, the core promise of the redesign — an assistant that doesn't compete with the viewer — fails immediately.

**Independent Test**: Load the Studio workspace fresh and confirm the chat widget renders only as the narrow Collapsed control, with the design viewer fully visible and unobstructed behind it.

**Acceptance Scenarios**:

1. **Given** a user opens the Studio workspace, **When** the page finishes loading, **Then** the chat widget appears in its Collapsed form by default.
2. **Given** the widget is Collapsed, **When** the user looks at it, **Then** it shows an expand handle, a real-time vertical voice analyzer, a Push-to-Talk control, a Continuous Listening toggle, a Mute Agent control, and a minimal status indicator — and nothing else.
3. **Given** the widget is Collapsed, **When** the user views the workspace, **Then** the design viewer remains fully visible and the widget does not overlap or obscure it.
4. **Given** the widget is Collapsed and idle, **When** the assistant is generating a response or actively capturing/playing audio, **Then** the voice analyzer visibly communicates the corresponding state (Idle, Processing, or Speaking/Listening) rather than staying static.

---

### User Story 2 - Expanding into the full conversation (Priority: P1)

A user activates the widget's handle and it smoothly grows into a floating conversation panel: the analyzer disappears, the message history and input become available, and voice controls remain reachable — all without disturbing the viewer underneath.

**Why this priority**: Expansion is the only way to actually read and compose messages; without it working smoothly and reliably the redesign delivers no usable chat experience at all.

**Independent Test**: From a Collapsed widget, activate the handle, confirm the panel expands to show conversation history, a message input, and voice controls with the analyzer gone, then collapse it again and confirm the workspace beneath is unaffected throughout.

**Acceptance Scenarios**:

1. **Given** the widget is Collapsed, **When** the user activates the expand handle, **Then** the widget smoothly transitions to its Expanded state.
2. **Given** the widget is Expanded, **When** the user looks at it, **Then** the vertical voice analyzer from the Collapsed state is no longer shown.
3. **Given** the widget is Expanded, **When** the user looks at it, **Then** it shows the assistant's identity and connection status, the active-language indicator, a scrollable list of user and assistant messages with timestamps, a text input for composing messages, and voice controls (Push-to-Talk, Continuous Listening) remain accessible.
4. **Given** the widget is Expanded, **When** the user activates the collapse action (the handle or an equivalent dismiss action), **Then** it smoothly returns to the Collapsed state without losing the in-progress conversation.
5. **Given** either state, **When** the transition between Collapsed and Expanded plays, **Then** it is smooth and animated rather than instant, except when the user has a reduced-motion preference enabled.
6. **Given** any state of the widget, **When** the user interacts with the underlying Studio viewer or its other contextual controls, **Then** the widget's presence does not alter the viewer's layout or block interaction with the rest of the workspace.

---

### User Story 3 - Starting fresh by default, with a minimal manual option (Priority: P2)

A user opens the Studio workspace and is already talking to the assistant in a brand-new conversation — no prominent button to press first, and no leftover conversation from a previous visit sitting in the way. If they later want to deliberately branch off into a fresh conversation without reloading the page, a small icon-only control tucked into the Expanded state (not the old prominent "+ New chat" button) lets them do that.

**Why this priority**: This changes a real behavior (not just a visual), removing a control users currently rely on, so it must land correctly to avoid confusing returning users — but it is secondary to the visual shell landing correctly first.

**Independent Test**: Load the Studio workspace and confirm a new, empty conversation is already active with no prominent "+ New chat" control present anywhere in the widget; confirm the minimal icon-only new-chat affordance in the Expanded state starts a fresh conversation on demand; confirm previously-held conversations are still reachable from Chat History in Settings.

**Acceptance Scenarios**:

1. **Given** the chat widget in either state, **When** the user looks for a way to start a new conversation, **Then** no prominent, text-labeled "+ New chat" control exists; only a minimal icon-only control in the Expanded state provides this action.
2. **Given** a user begins a new session in the Studio workspace, **When** the widget is expanded, **Then** a new, empty conversation is already the active one, with no action required to create it.
3. **Given** a user wants to revisit a prior conversation, **When** they look in the chat widget, **Then** it is not there; **When** they open Chat History in Settings, **Then** their prior conversations are listed and reachable exactly as before this change.
4. **Given** the widget is Expanded with an active conversation, **When** the user activates the minimal new-chat icon, **Then** a new, empty conversation becomes the active one without a page reload, and the conversation it replaced remains fully reachable via Chat History in Settings.

---

### User Story 4 - Seeing the active language as a flag, changed only from Settings (Priority: P2)

A user glances at the assistant header and sees a small circular flag representing the language the assistant is currently responding in — there is no dropdown to fiddle with in the chat widget itself; changing the language happens once, in Chat Configuration.

**Why this priority**: Removing the inline dropdown simplifies the widget's chrome per the redesign's intent, but the feature is only complete once there's a working replacement path (Settings) for the control being removed.

**Independent Test**: Confirm no language dropdown exists in either widget state; confirm a flag icon reflecting the current language appears in the Expanded header; change the default language in Chat Configuration and confirm the flag updates to match.

**Acceptance Scenarios**:

1. **Given** the chat widget in either state, **When** the user looks for a language control, **Then** no dropdown menu is present.
2. **Given** the widget is Expanded, **When** the user looks at its header, **Then** a small circular flag icon representing the currently active response language is shown.
3. **Given** a user opens Chat Configuration in Settings, **When** they change their default response language, **Then** the change is saved as their default for the assistant.
4. **Given** the user has changed their default language in Chat Configuration, **When** they view the chat widget again, **Then** the flag icon reflects the newly selected language.

---

### User Story 5 - Reviewing a voice message before it's transcribed (Priority: P2)

A user presses Push-to-Talk (the "record" control), speaks, and — instead of their words being silently captured and immediately turned into text — sees a live waveform of their speech only, taps a control to say they're done, and is then given a clear choice: cancel and go back to typing, or accept and send the recording to be transcribed. No audio is transcribed or leaves the device until they explicitly accept. This review flow is specific to Push-to-Talk; Continuous Listening's always-on, hands-free behavior is unaffected and continues to work exactly as it does today.

**Why this priority**: This directly replaces an existing interaction (immediate live capture) with a review-and-confirm step; getting the three-button flow right matters for trust in voice input, but the feature is usable without it if the simpler capture still works underneath.

**Independent Test**: Start a Push-to-Talk voice recording from either widget state, confirm a live waveform (and no live partial transcript) displays while speaking, tap the "finished speaking" control, confirm cancel discards the recording and returns to typing with no text inserted and nothing sent anywhere, and confirm accept/send is the only action that transmits the recording for transcription, with its result used exactly as voice input is used today. Separately, confirm Continuous Listening keeps behaving exactly as it did before this feature, with no review step.

**Acceptance Scenarios**:

1. **Given** the user starts Push-to-Talk voice capture, **When** they are speaking, **Then** a live waveform of the captured audio is displayed and no live partial transcript is shown.
2. **Given** a recording is in progress, **When** the user activates the "finished speaking" control, **Then** capture stops and the recording enters a review state without having been transmitted anywhere yet.
3. **Given** a recording is in the review state, **When** the user activates cancel, **Then** the captured audio is discarded on the device, no transcript is inserted, nothing is sent to a transcription service, and the interface returns to normal typing mode.
4. **Given** a recording is in the review state, **When** the user activates send/accept, **Then** the recording is submitted for transcription for the first time, and its result is used exactly as existing voice-to-text input is used today.
5. **Given** a recording is in progress or in review, **When** the user collapses the widget, **Then** the in-progress or unreviewed recording is discarded rather than continuing invisibly, and the widget returns to its normal Collapsed idle state.
6. **Given** the user is in Continuous Listening mode, **When** they speak, **Then** capture and recognition continue to behave exactly as before this feature — always-on, with no waveform-review, finish, cancel, or send step.

---

### User Story 6 - No standalone image-generation button (Priority: P3)

A user composing a message no longer sees a dedicated "Generate image" button in the chat widget; image generation, when it becomes available, will be something the assistant does from within the conversation itself.

**Why this priority**: This is a small removal with no replacement UI to build in this feature — lowest risk, least disruptive to existing workflows.

**Independent Test**: Confirm no "Generate image" control exists anywhere in the chat widget, in either state.

**Acceptance Scenarios**:

1. **Given** the chat widget in either state, **When** the user looks at the available composer actions, **Then** no standalone "Generate image" button is present.

---

### Edge Cases

- What happens if the browser denies microphone access when the user tries Push-to-Talk, Continuous Listening, or a voice recording? The existing permission-denied messaging continues to apply; the widget does not silently fail.
- What happens if a user has never set a default language? A sensible default (matching the assistant's current default) is shown as the flag until the user changes it in Chat Configuration.
- What happens on a user's very first message in a freshly auto-started conversation? The Expanded state shows the assistant's normal greeting/empty state, consistent with prior behavior.
- What happens if the user mutes the agent mid-conversation? Audio output stops while text messages continue to send, stream, and display normally.
- What happens if the user rapidly toggles expand/collapse while a transition is still animating? The widget settles into a single consistent state (fully Expanded or fully Collapsed) rather than getting stuck mid-transition.
- What happens if the user starts a Push-to-Talk recording from the Collapsed control and then expands the widget mid-recording (or vice versa)? The recording and its review controls remain consistent and available in both states using the same visual language.

## Requirements *(mandatory)*

### Functional Requirements

**Widget shell and states**

- **FR-001**: The Studio workspace's chat entry point MUST render as a floating widget with exactly two states, Collapsed and Expanded, replacing its current presentation with the redesigned widget described here; other Studio contextual controls (view mode, layers, navigation, selection, analysis, account) are unaffected by this feature.
- **FR-002**: The chat widget MUST default to the Collapsed state whenever the Studio workspace is loaded.
- **FR-003**: The Collapsed state MUST display, at minimum: an expand/collapse handle, a real-time vertical voice analyzer, a Push-to-Talk control, a Continuous Listening toggle, a Mute Agent control, and a minimal status indicator.
- **FR-004**: The voice analyzer MUST visually communicate at least three distinct states — Idle, Processing, and Speaking/Listening — and MUST update as the underlying state changes.
- **FR-005**: The Collapsed widget MUST remain visually narrow and lightweight, and MUST NOT overlap or obscure the primary Studio design viewer.
- **FR-006**: Activating the handle MUST expand the widget to the Expanded state; activating it again, or an equivalent dismiss action, MUST collapse it back to the Collapsed state, without discarding the conversation in progress.
- **FR-007**: The Expanded state MUST NOT display the vertical voice analyzer shown in the Collapsed state.
- **FR-008**: The Expanded state MUST display: the assistant's identity and connection/online status, the active-language flag indicator (FR-016–FR-017), a scrollable list of user and assistant messages with timestamps, a text input for composing messages, and voice controls (Push-to-Talk, Continuous Listening) remaining reachable.
- **FR-009**: Transitions between Collapsed and Expanded MUST use smooth, animated motion rather than appearing/disappearing instantly, except when the user has a reduced-motion preference enabled.
- **FR-010**: Every control in both states MUST be fully operable via mouse/pointer, touch, and keyboard alone (focus, activate with Enter/Space, dismiss with Escape or an equivalent action).
- **FR-011**: The widget MUST remain positioned above the Studio viewer without altering the viewer's layout or blocking interaction with the rest of the workspace, in either state.

**Starting a new conversation by default**

- **FR-012**: The chat widget MUST NOT present the previous prominent, text-labeled "+ New chat" control, in either state.
- **FR-013**: Each time a user begins a new session in the Studio workspace, the system MUST automatically make a new, empty conversation the active one, without requiring a manual action; access to previously-held conversations remains available only through the existing Chat History area in Settings, unchanged by this feature.
- **FR-014**: The Expanded state MUST provide a minimal, icon-only control for manually starting a new conversation mid-session without a page reload; activating it MUST make a new, empty conversation the active one and MUST NOT delete or hide the conversation it replaced, which remains reachable via Chat History in Settings.

**Active language as a flag**

- **FR-015**: The chat widget MUST NOT present a language dropdown menu, in either state.
- **FR-016**: The Expanded state's header MUST show a small circular flag icon representing the user's currently active default response language.
- **FR-017**: The user's default response language MUST be settable only from the Chat Configuration section of Settings; when changed there, the flag icon shown in the chat widget MUST reflect the new selection.

**Removing the image-generation button**

- **FR-018**: The chat widget MUST NOT present a standalone "Generate image" control, in either state; this feature does not implement an in-conversation, agent-triggered replacement — only removes the existing button.

**Voice recording review flow (Push-to-Talk only)**

- **FR-019**: When the user initiates Push-to-Talk voice capture, the system MUST buffer the captured audio on the client and display a live waveform reflecting it while recording is in progress; no live partial transcript is shown, and no audio is transmitted to any transcription service at this point.
- **FR-020**: While recording, the user MUST have a clearly labeled control indicating they are finished speaking; activating it MUST stop capture and move the recording into a review state, still without transmitting the audio anywhere.
- **FR-021**: In the review state, the user MUST have a control to cancel the recording; activating it MUST discard the captured audio on the device, insert no transcript, send nothing to any transcription service, and return the interface to normal typing mode.
- **FR-022**: In the review state, the user MUST have a control to accept and send the recording for transcription; this explicit action MUST be the only trigger that transmits the recording for transcription, with the result used exactly as existing voice-to-text input is used today.
- **FR-023**: The recording, review, cancel, and send controls MUST use the same visual language and behavior whether Push-to-Talk is initiated from the Collapsed control or from the Expanded state's voice controls.
- **FR-024**: Collapsing the widget while a Push-to-Talk recording is in progress or awaiting review MUST discard that recording rather than allowing it to continue capturing while hidden.
- **FR-025**: Continuous Listening MUST retain its existing always-on, live-recognition behavior exactly as it works today; the waveform/finish/cancel/send review flow (FR-019–FR-024) MUST NOT apply to it.

**Preserving existing behavior**

- **FR-026**: This feature MUST NOT change the underlying AI/LLM request handling, response streaming, message persistence, or provider/model selection behavior — only the chat widget's presentation and interaction model change.
- **FR-027**: All existing chat capabilities not explicitly altered by this specification (sending messages, streamed responses, attachments, saved-prompt insertion, viewing message history) MUST continue to work unchanged once presented through the redesigned widget.

### Key Entities

- **Chat Widget**: The floating assistant control living over the Studio workspace, with exactly two states — Collapsed and Expanded — that together replace the previous chat panel's presentation.
- **Voice Analyzer**: The Collapsed-state visual indicator of the user/assistant's real-time audio state (Idle, Processing, Speaking/Listening); not shown while Expanded.
- **Recording Review**: The transient, Push-to-Talk-only state entered after the user indicates they've finished speaking; the captured audio stays buffered on the device — never transmitted for transcription — until the user explicitly accepts, at which point cancel and accept/send are the only ways out of this state. Continuous Listening does not use this entity; it keeps its existing always-on, live-recognition behavior.
- **Active Language Indicator**: The circular flag icon shown in the Expanded header, reflecting the user's current default response language as set in Chat Configuration.
- **Conversation**: The existing chat/message entity (unchanged data model); this feature changes only how a conversation is started (automatically) and how it is presented (through the redesigned widget).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On every load of the Studio workspace, the chat widget appears in its Collapsed form and does not visibly overlap the design viewer, verified across desktop, tablet, and mobile widths.
- **SC-002**: Users can move between Collapsed and Expanded in a single interaction, with the transition completing smoothly (no visible stutter) on typical modern desktop and mobile hardware.
- **SC-003**: 100% of new Studio sessions begin with an already-active, empty conversation, with no prominent "+ New chat" control present anywhere in the widget; users can still start a new conversation mid-session via the minimal icon-only control in the Expanded state.
- **SC-004**: 100% of default-language changes made in Chat Configuration are reflected by the flag icon in the chat widget without further action from the user.
- **SC-005**: Zero Push-to-Talk voice recordings are transmitted for transcription without the user having taken an explicit accept/send action; users can cancel a recording and return to typing, with nothing sent anywhere, at any point before sending. Continuous Listening is unaffected and requires no such confirmation.
- **SC-006**: Existing chat functionality (sending a message, receiving a streamed reply, attachments, saved prompts) works with zero regressions after this redesign, verified against pre-redesign behavior.
- **SC-007**: Every interactive element of the widget, across both states and the recording review flow, can be reached and fully operated using only a keyboard or only touch, matching what's possible with a mouse.

## Assumptions

- The supplied readdy.ai preview page renders its UI client-side; it could not be retrieved as static markup/CSS for a programmatic scan, so the visual and interaction details in this specification (handle, vertical analyzer, Push-to-Talk/Listening/Mute controls, status label, and the Expanded header/message/input/footer layout) are derived from the four reference screenshots supplied alongside the feature request, refined to fit the existing design system rather than copied pixel-for-pixel — consistent with how spec 024 (Flumeria Studio Workspace Shell) already treats its own reference design.
- This feature specializes the presentation of the existing chat entry point established by spec 024 (`FR-013`); it does not change how the other Studio contextual controls (view mode, layers, navigation, selection, analysis, account) look or behave.
- "New session" for the auto-new-conversation behavior means each time the user loads (or reloads) the Studio workspace — matching what the previous manual "+ New chat" button effectively did — not a boundary drawn around every individual message.
- Chat History in Settings (introduced by spec 025) already provides the only way to browse and reopen prior conversations; this feature does not add or change any conversation-switching UI inside the chat widget itself, consistent with that existing design.
- Chat Configuration (spec 025) does not currently expose a default-language control; this feature adds one there, reusing the existing set of supported languages already defined in the product unless Settings work independently expands that list.
- The Mute Agent control mutes the assistant's voice/audio output only; it does not stop text responses from being generated or displayed.
- Removing the "Generate image" button is a removal only — this feature does not implement the future agent-triggered, in-conversation image generation referenced as its replacement.
- Microphone-permission handling and the existing audio-transcription endpoint are reused as-is. Push-to-Talk's underlying capture mechanism changes from today's live, incremental speech recognition to a discrete record-then-transcribe flow (buffer locally, transmit only on accept — see Clarifications), while Continuous Listening's live-recognition mechanism is untouched by this feature.
- Visual and interaction changes described here apply to the authenticated Studio workspace only; no other page is affected.
