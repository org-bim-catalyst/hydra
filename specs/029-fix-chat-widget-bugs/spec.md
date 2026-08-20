# Feature Specification: Chat Widget Reliability & Voice UI Consolidation

**Feature Branch**: `029-fix-chat-widget-bugs`

**Created**: 2026-08-20

**Status**: Draft

**Input**: User description: "Fix four production bugs in the Ask Lucy chat widget: (1) chat window shows a scary generic error banner on every load caused by a failed personalization-data fetch, (2) the voice input area shows two separate, uncoordinated sets of microphone/recording controls at once, (3) the translate control sits above the message history and wastes vertical space, and (4) real-time/live-update connections used by chat-adjacent features fail in production. Use Claude's own desktop chat UI as the reference for a single, consolidated voice-input control (one mic control, one recording/listening state, one set of confirm/cancel actions)."

## Clarifications

### Session 2026-08-20

- Q: Should the fix for the personalization-settings failure include a safeguard against recurrence, or just resolve this one instance? → A: Fix this instance, plus add a safeguard so schema drift on this specific data path is caught fast (e.g., a startup/readiness check) rather than surfacing as a live-request failure — not a platform-wide schema-drift safeguard for every table/endpoint.
- Q: Where exactly should the translate control move to? → A: Merged into the same row as the composer/voice controls (e.g., a small icon alongside the mic/attach/send controls), not a separate dedicated row.
- Q: How should mute and the continuous/push-to-talk mode setting be exposed on the consolidated mic control? → A: Not as two extra always-visible icons layered on top of the mic. The single mic control's icon and behavior change with context: in Continuous mode the mic icon itself is the listening on/off toggle for the session (tapping it pauses/resumes listening without ending the session — colloquially "mute the mic"); in Push-to-Talk mode, engaging the mic starts recording and its icon area is replaced by Cancel (X) / Confirm (✓) actions during recording/review. Switching between Continuous and Push-to-Talk ("hold to record") is a setting reached via a menu/affordance attached to the mic control (alongside input-device selection), not a separate persistent icon.
  - **Correction found during planning research**: the *existing* "mute" toggle in the current UI (the speaker/volume icon) does not control the microphone at all — it mutes Lucy's spoken voice reply (text-to-speech output), a wholly separate, still-needed capability. It is unaffected by this consolidation: it MUST remain available as its own small, persistent control in the composer/voice-control row (not folded into the mic control or its menu), since it is unrelated to the mic's own listening/recording state.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Real-time features work reliably in production (Priority: P1)

Any feature in the workspace that depends on a live, persistent connection to the server (live floating panels, workflow progress, document-processing status, memory updates, agent-execution updates) must actually connect every time a user opens the app, instead of silently failing to connect.

**Why this priority**: This is the widest-blast-radius issue — it affects every feature built on a live connection, not just one screen, and currently fails 100% of the time in production. Nothing that depends on real-time updates can be trusted until this is fixed.

**Independent Test**: Open the app in production, trigger any feature that relies on live server updates (e.g., open a floating panel, start a workflow, upload a document for processing), and confirm the live connection is established and stays connected without manual refresh.

**Acceptance Scenarios**:

1. **Given** a user opens the app in production, **When** any feature that needs a live connection activates, **Then** the connection is established successfully on the first attempt.
2. **Given** a live connection attempt fails for a genuine reason (e.g., network outage), **When** the failure occurs, **Then** the user is shown a clear, visible indication that live updates are unavailable, rather than the failure happening with no trace anywhere.
3. **Given** the fix is deployed, **When** any of the app's live-update features are used (not just the one originally reported), **Then** all of them connect successfully, since the same underlying defect affects all of them equally.

---

### User Story 2 - Chat opens without a false error message (Priority: P1)

A user opening the chat window must land in a clean, ready-to-use conversation view. Today, every single chat load shows a generic "An unexpected error occurred" banner triggered by a failed attempt to load the user's personalization settings (voice preferences) — even though the chat itself works fine with default settings.

**Why this priority**: This is the very first thing every user sees on every chat open, and it currently reads as a broken product on 100% of loads, even though nothing is actually broken from the user's point of view.

**Independent Test**: Open the chat window as any user and confirm no error banner appears, while confirming voice-related settings still function using sensible defaults.

**Acceptance Scenarios**:

1. **Given** a user opens the chat window, **When** the chat panel loads, **Then** no generic/alarming error banner is shown as a result of personalization settings being unavailable.
2. **Given** personalization settings fail to load, **When** the chat falls back to default settings, **Then** the chat and voice features remain fully usable with those defaults.
3. **Given** personalization settings genuinely fail to load, **When** the failure happens, **Then** it is still recorded/traceable on the server so the underlying problem can be diagnosed and fixed — it is only the user-facing alarming banner that goes away, not the failure's visibility to the team operating the system.
4. **Given** the personalization-settings data path drifts out of sync again in the future, **When** that drift occurs, **Then** it is caught by a dedicated safeguard (e.g., at startup or via a readiness check) before it can manifest as a live-request failure for an end user.

---

### User Story 3 - One clear voice recording control (Priority: P2)

A user recording a voice message must see exactly one microphone control and one recording status, not two separate, uncoordinated sets of controls reacting to the same action.

**Why this priority**: This is confusing and looks broken every time voice input is used, though it doesn't block the user from completing the action (unlike Story 1 and 2, which affect every session before any user action is taken).

**Independent Test**: Tap the microphone control in the chat composer and confirm only one recording/listening indicator and one set of cancel/confirm actions appears, matching the single-control experience of a modern reference chat product (e.g., Claude's own chat interface: one mic control whose icon and behavior adapt to the current mode, plus a menu for device/mode settings, a single waveform/listening state, and one confirm/cancel action set).

**Acceptance Scenarios**:

1. **Given** a user is viewing the chat composer, **When** they look for a way to record a voice message, **Then** there is exactly one microphone control visible.
2. **Given** a user starts recording, **When** the recording is in progress, **Then** exactly one recording-status indicator (with waveform/elapsed state) and one set of cancel/confirm controls is shown — never two.
3. **Given** the mode is set to Continuous listening, **When** the user taps the mic control, **Then** it mutes/unmutes the microphone for the ongoing session in place (its icon reflects listening vs. muted) — this is the *microphone* mute (stop picking up the user's voice / stop Lucy from listening), without a separate always-visible icon for it.
4. **Given** the mode is set to Push-to-Talk, **When** the user engages the mic control, **Then** recording begins and the control's icon area is replaced by Cancel (X) and Confirm (✓) actions for the duration of recording and review.
5. **Given** the previous toolbar exposed a continuous-vs-push-to-talk mode toggle as its own icon, **When** the controls are consolidated, **Then** mode-switching is reachable via a menu/settings affordance attached to the mic control rather than a separate persistent icon.
6. **Given** the previous toolbar exposed both a speaker-mute toggle and a separate "stop the current reply" action, **When** the controls are consolidated, **Then** they are merged into one always-visible speaker icon: pressing it mutes immediately (silencing a reply Lucy is currently speaking, if any, right away) and keeps her silent — she still responds in text — for every subsequent reply until the same icon is pressed again to unmute. This single toggle is independent from the microphone mute in Scenario 3 (one silences what the user's mic picks up, the other silences what Lucy speaks back).
7. **Given** a user cancels or confirms a recording, **When** that action completes, **Then** the single control returns cleanly to its idle state with no leftover UI from a second control.
8. **Given** Lucy is actively speaking a reply aloud, **When** the user looks at the chat composer/voice-control row, **Then** no text label there restates "Lucy is speaking…" — that state is already conveyed by the persistent, always-visible reactive presence indicator elsewhere on screen (the sphere), which is unaffected by this feature and unrelated to the chat panel's own expand/collapse state.
9. **Given** the mic is actively capturing the user's voice, **When** the user looks at the mic control, **Then** no "Listening…" text label appears beside it — the mic icon's own active-state visual (e.g. its existing pulse animation) is sufficient on its own to convey that it's listening.

---

### User Story 4 - More of the conversation is visible at a glance (Priority: P3)

A user scrolling through their conversation history should see as much of it as reasonably fits on screen. Today, a translate control sits in its own row above the message history, taking up vertical space that could show more of the conversation. It will move into the same row as the composer/voice controls, rather than keeping a dedicated row of its own.

**Why this priority**: This is a space/polish improvement rather than a functional break — nothing is broken, but the layout could be more efficient.

**Independent Test**: Open a conversation with enough messages to scroll, and confirm more message content is visible above the fold after the translate control is relocated into the composer/voice-control row.

**Acceptance Scenarios**:

1. **Given** a user opens a conversation, **When** the chat window renders, **Then** the translate control appears within the composer/voice-control row below the message history, not in a dedicated row above it.
2. **Given** the translate control has moved, **When** the user compares before and after, **Then** the message-history area is taller / shows more content in the same window, since its dedicated row has been removed entirely.
3. **Given** a user activates the relocated translate control, **When** they use it, **Then** it behaves exactly as it did before the move (same action, same result) — only its position has changed.
4. **Given** the composer/voice-control row now also hosts the translate control, **When** it is rendered alongside the consolidated voice control (User Story 3), **Then** the two remain visually distinct and do not crowd or overlap each other.

---

### Edge Cases

- What happens if personalization (voice preference) data remains unavailable indefinitely, not just on one load? The chat and voice features must continue operating on defaults indefinitely, without the user ever being blocked or repeatedly alarmed.
- What happens if a user has no microphone connected, or denies microphone permission, when using the consolidated voice control? The single control must communicate this clearly through one coherent state, not through mismatched signals from two different controls.
- What happens when multiple live-update features are active at the same time (e.g., a floating panel open while a workflow is running)? Each must connect and recover independently; a failure in one must not be mistaken for or mask a failure in another.
- What happens if a live connection drops after initially succeeding (e.g., temporary network blip)? The affected feature must attempt to reconnect and only surface a user-visible failure indication if reconnection does not succeed, not on every transient blip.
- What happens when a conversation has too few messages to need scrolling? The relocated translate control must still be reachable and must not overlap the composer or recording controls.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The chat window MUST become fully usable without displaying a generic, alarming error banner caused solely by a failed attempt to load personalization (voice preference) settings.
- **FR-002**: When personalization settings cannot be loaded, the system MUST fall back to safe default settings automatically, and chat and voice functionality MUST remain fully usable under those defaults.
- **FR-003**: A failure to load personalization settings MUST still be recorded server-side in a way the operating team can detect and diagnose — removing the user-facing banner MUST NOT remove the underlying failure's traceability, consistent with the platform's no-silent-failure standard.
- **FR-004**: The chat composer MUST present exactly one microphone control at all times — never two independent controls performing the same action simultaneously.
- **FR-005**: While recording a voice message, the system MUST show exactly one recording/listening status indicator and one set of cancel/confirm actions, consolidated into a single location.
- **FR-006**: All voice-related capabilities currently available (start/stop/cancel recording, pausing/resuming listening in Continuous mode, switching between continuous and push-to-talk modes) MUST remain available after consolidation into the single mic control, without adding extra always-visible icons for those specifically: the listening pause/resume state MUST be expressed through the mic control's own contextual state while in Continuous mode, and mode-switching MUST be reachable through a menu/settings affordance attached to the mic control rather than a separate persistent icon.
- **FR-006a**: The speaker-output mute control and the "stop the current reply" action MUST be merged into a single, always-visible toggle icon in the composer/voice-control row: pressing it while Lucy is speaking immediately silences that reply *and* sets her to muted; pressing it at any other time simply sets her to muted; either way she keeps responding in text, and no reply is spoken aloud again until the same icon is pressed to unmute. This toggle is unrelated to the microphone and MUST NOT be folded into the mic control's menu or conflated with the microphone's own mute behavior (FR-006).
- **FR-006b**: There MUST NOT be a second, separate control for interrupting only the current reply — muting via FR-006a's single icon is the only and sufficient way to do so.
- **FR-013**: The chat composer/voice-control row MUST NOT show a text label restating that Lucy is speaking (e.g. "Lucy is speaking…") — that information is already conveyed by the platform's existing, always-visible reactive presence indicator (the sphere), which this feature does not modify.
- **FR-014**: The chat composer/voice-control row MUST NOT show a text label restating that the mic is actively capturing (e.g. "Listening…") — the mic control's own visual state (its existing active/pulsing appearance) MUST be sufficient on its own to convey that it is listening.
- **FR-007**: The translate control MUST be relocated into the composer/voice-control row below the message history — not kept in a dedicated row of its own — with its existing behavior unchanged.
- **FR-008**: Relocating the translate control MUST result in more vertical space being available for the message history.
- **FR-009**: Every feature in the application that depends on a persistent, real-time server connection MUST be able to establish that connection successfully in production.
- **FR-010**: If a real-time connection attempt fails, the system MUST surface a clear, user-visible indication of the failure rather than failing with no visible trace, consistent with the platform's no-silent-failure standard. Concretely, connection state MUST be exposed to the component consuming it, not discarded — matching the pattern already used by 3 of the app's 5 current hub consumers (`useWorkflowExecutionHub`, `useDocumentProcessingHub`, `useAgentExecutionHub` already expose an `isLive` state; `useFloatingPanelHub`, `useMemoryNotificationsHub`, and the document-notifications consumer `useNotificationHub` currently discard connection failures via `.catch(() => undefined)` and must be brought in line with the other three).
- **FR-011**: The real-time connection fix MUST apply uniformly to every feature that depends on such a connection, not only the specific feature in which the failure was first observed.
- **FR-012**: The system MUST include a safeguard that detects drift on the personalization-settings data path (e.g., at startup or via a readiness check) so such drift is caught before it can manifest as a live-request failure for an end user. This safeguard is scoped to the personalization-settings data path, not a platform-wide schema-drift detection mechanism.

### Key Entities

- **Voice Preference**: A user's saved voice/personalization settings (e.g., preferred recording mode, language). May be temporarily unavailable; the system must have a well-defined default to fall back to.
- **Real-time Connection**: A persistent, live channel between the client and server that powers any feature needing live updates (panels, workflow progress, document processing, memory, agent execution). Each instance connects, may disconnect/reconnect, and independently reports its own success or failure state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 0% of chat-window loads display a generic "unexpected error" banner caused by personalization-settings failures (down from effectively 100% today).
- **SC-002**: In 100% of manual verification passes, the chat composer shows exactly one microphone control and one recording-status indicator at a time.
- **SC-003**: All real-time/live-update features connect successfully on first attempt, verified via manual QA across every hub-backed feature per quickstart.md §5 for this release (the app has no client-side production telemetry pipeline today to automatically measure a "99% of sessions" figure — that would require new Analytics Engine work outside this bug-fix feature's scope; a follow-up feature should add that instrumentation if ongoing automated measurement is wanted).
- **SC-004**: The message-history viewport shows measurably more content (additional message rows visible without scrolling) after the translate control is relocated, on a standard chat-window viewport size.
- **SC-005**: Reports of duplicated voice controls or false "unexpected error" banners on chat load drop to zero after release.

## Assumptions

- The chat and voice experience already has reasonable default settings to fall back on when personalization data is unavailable; this feature reuses those defaults rather than introducing new ones.
- "Below the message history" for the translate control means merged into the same composer/voice-control row at the bottom of the chat window (not a separate dedicated row), consistent with common chat-app layouts and the reference product cited by the user.
- This is a consolidation of two existing, duplicated voice-control UIs into one — no new voice-recording capability is being introduced. Microphone mute and mode-switching (FR-006) change how the single mic control looks/behaves rather than adding new persistent icons next to it. The previously separate speaker-output mute and "stop current reply" controls are deliberately merged into one always-visible speaker icon (FR-006a/FR-006b), per explicit direction: this is an intentional behavior simplification, not an oversight, and is kept visually and functionally distinct from the mic's own microphone-mute behavior (FR-006). The "Lucy is speaking…" text label is removed outright, with no replacement needed inside the chat panel (FR-013): Lucy's speaking state is already conveyed by the platform's persistent reactive presence indicator (the sphere, driven by the same TTS intensity signal), which renders independently of the chat panel's expand/collapse state and is unaffected by this feature. The "Listening…" text label is likewise removed outright (FR-014): the mic control's own existing active-state visual already conveys that it's capturing, without needing a text restatement alongside it.
- The real-time connection defect is a single shared root cause affecting all live-update features uniformly; fixing it once resolves all of them rather than requiring a separate fix per feature.
- Scope is limited to these four fixes; broader visual redesign of the chat window beyond relocating the translate control and consolidating voice controls is out of scope.
