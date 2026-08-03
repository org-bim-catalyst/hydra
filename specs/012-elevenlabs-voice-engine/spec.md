# Feature Specification: ElevenLabs Conversational Voice Engine

**Feature Branch**: `012-elevenlabs-voice-engine`

**Created**: 2026-08-02

**Status**: Draft

**Input**: User description: "Replace the current Speech-to-Text (STT) and Text-to-Speech (TTS) implementation with the ElevenLabs Audio Engine and build a production-quality conversational voice system comparable to ChatGPT Advanced Voice Mode. This is a production migration, not a proof of concept. The existing voice system should be completely replaced while preserving the rest of the AI agent architecture. Support streaming STT/TTS, natural turn-taking, interruptible AI speech, Continuous Conversation Mode, Push-to-Talk Mode, low latency, real-time audio visualization integrated into the existing Three.js analyzer sphere (not replaced), voice controls (mic states, mode toggle, mute, stop), persisted voice preferences, a centralized voice state machine, and future support for additional speech providers."

## Clarifications

### Session 2026-08-02

- Q: ElevenLabs bills per character/audio-minute, unlike today's free local Whisper.net STT and free browser TTS. Should the feature be available to all users, or limited by subscription tier? → A: All users get access at launch; cost is controlled only through the platform's existing usage-monitoring/rate-limiting, with tier-based caps deferred unless usage data later shows a need.
- Q: Which languages must the new voice engine support at launch? → A: Full parity with whatever languages the product's UI/localization already supports today — no narrower language scope than the system being replaced.
- Q: What should happen if the voice provider becomes unreachable (outage, rate limit, network failure) mid-session? → A: Revised 2026-08-02 — automatically and transparently fall back to the existing browser-based (Whisper.net STT / browser `speechSynthesis` TTS) implementation as a degraded-but-functional backup, with a visible notice that voice quality is temporarily reduced. The legacy voice path is therefore kept in production indefinitely as a permanent fallback, not retired.
- Q: When a session has failed over to the legacy fallback engine, should it automatically retry and switch back to the primary provider during the same session, or stay on the fallback until a new session starts? → A: Automatically retry the primary provider and transparently switch back if it recovers, before the user's next voice turn, with no manual action required.
- Q: Should administrators have any visibility when voice sessions are failing over to the legacy engine? → A: Yes — surface fallback frequency/status to administrators, mirroring the existing multi-provider AI health-monitoring pattern already in the product.
- Q: Should the legacy fallback voice preserve the same branded persona as the primary voice, or is a generic/different voice acceptable in degraded fallback mode? → A: The fallback MUST preserve the same persona-matching behavior the legacy engine already uses today — no exception for degraded mode.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Push-to-Talk voice exchange with natural speech (Priority: P1)

A user clicks the microphone button, speaks a message, and hears the AI respond in a natural-sounding voice that starts speaking almost immediately rather than after a long pause — replacing today's robotic, browser-default voice and a transcription step that only returns a result after the user manually stops recording.

**Why this priority**: This is the smallest slice that already delivers the feature's core value — noticeably better-sounding, faster-starting voice interaction — using an interaction pattern (click to talk, click to stop) that users already know. It does not require Continuous Conversation Mode or interruption support to be useful and demonstrable on its own.

**Independent Test**: Can be fully tested by clicking the microphone, speaking a short sentence, and confirming (a) a transcript of what was said appears, (b) the AI's spoken reply begins playing before the full reply has finished generating, and (c) the voice sounds natural and consistent rather than like the browser's built-in voice.

**Acceptance Scenarios**:

1. **Given** the microphone is idle, **When** the user clicks it and speaks a complete sentence, **Then** the system captures the audio, produces a text transcript of what was said, and uses that transcript to generate the AI's response.
2. **Given** the AI has begun generating a response, **When** the first portion of the spoken reply is ready, **Then** audio playback begins immediately without waiting for the entire response to finish generating.
3. **Given** a voice reply is playing, **When** it finishes, **Then** the microphone returns to its idle, inactive state and the user must click it again to speak another message.
4. **Given** the user has finished speaking, **When** they stop talking, **Then** the system automatically detects the end of speech and begins processing without requiring the user to manually signal "done" (e.g., a second click), unless they choose to end capture manually.

---

### User Story 2 - Hands-free Continuous Conversation Mode (Priority: P2)

A user turns on Continuous Conversation Mode and has a back-and-forth spoken conversation with the AI without touching any button between turns — speaking, waiting for the reply, and speaking again, the way they would talk to another person on a call.

**Why this priority**: This is the flagship experience the feature is named for (comparable to ChatGPT Advanced Voice Mode) and the biggest differentiator, but it depends on the streaming STT/TTS foundation delivered in User Story 1 and adds meaningfully more complexity (automatic speech detection, automatic re-listening), so it is sequenced after the simpler interaction pattern is proven.

**Independent Test**: Can be fully tested by enabling Continuous Conversation Mode, speaking a message without clicking anything, letting the AI respond, and confirming the system automatically starts listening again afterward — repeated across at least two full turns with no manual interaction required between them.

**Acceptance Scenarios**:

1. **Given** Continuous Conversation Mode is active and the microphone is enabled, **When** the user begins speaking, **Then** the system automatically detects the start of speech without any button press.
2. **Given** the user has finished speaking, **When** a pause consistent with the end of an utterance is detected, **Then** the system automatically stops capturing and begins processing the message.
3. **Given** the AI's spoken reply has finished playing, **When** no other action occurs, **Then** the system automatically resumes listening for the user's next turn without requiring the user to click anything.
4. **Given** the user is in Continuous Conversation Mode, **When** they explicitly exit voice mode, **Then** the automatic listen-respond loop stops and the microphone returns to idle.

---

### User Story 3 - Natural interruption of AI speech (Priority: P3)

While the AI is speaking, the user starts talking — to correct something, ask a follow-up, or redirect the conversation — and the AI immediately stops talking and starts listening, without the user needing to press a stop button first.

**Why this priority**: This is what makes the conversation feel natural rather than like a walkie-talkie exchange, but it is an enhancement layered on top of both interaction modes (Stories 1 and 2) rather than a prerequisite for either to deliver value.

**Independent Test**: Can be fully tested by having the AI begin an audio reply, speaking over it partway through, and confirming the AI's audio stops immediately, no further audio from that reply plays, and the system begins listening to the new input.

**Acceptance Scenarios**:

1. **Given** the AI is speaking, **When** the user begins speaking, **Then** AI audio playback stops immediately and no further audio from that reply is generated or queued.
2. **Given** an interruption has occurred, **When** the interruption is detected, **Then** the system immediately begins capturing the user's new speech instead of requiring a manual stop action first.
3. **Given** the user interrupts and then speaks a new message, **When** that message is processed, **Then** the AI responds to the new message; the interrupted reply is not resumed or replayed.

---

### User Story 4 - Voice controls: mute, stop, and mode switching (Priority: P4)

A user manages an in-progress or upcoming voice interaction using dedicated controls: muting audio output without stopping the AI from "thinking," stopping an in-progress AI reply outright, and switching between Push-to-Talk and Continuous Conversation Mode without losing their place in the conversation.

**Why this priority**: These controls make the two core interaction modes (Stories 1–2) safe and comfortable to use in real settings (shared offices, needing to read instead of listen, changing one's mind about interaction style), but the modes remain independently valuable without them, so this is sequenced last.

**Independent Test**: Can be fully tested by (a) muting during an AI reply and confirming the reply keeps generating and the visualization keeps reacting while no sound plays, (b) stopping an in-progress reply and confirming playback and generation both halt immediately, and (c) switching conversation mode mid-conversation and confirming the existing conversation history and context are unaffected.

**Acceptance Scenarios**:

1. **Given** the AI is speaking, **When** the user mutes audio output, **Then** sound stops immediately but the AI continues generating its response and the voice-reactive visualization continues responding to the generated audio.
2. **Given** audio output is muted, **When** the user unmutes, **Then** subsequent AI replies play normally; the muted reply is not replayed retroactively.
3. **Given** the AI is speaking, **When** the user presses stop, **Then** playback stops immediately, no further audio for that reply is generated, the visualization returns to its idle state, and — if Continuous Conversation Mode is active — listening resumes automatically.
4. **Given** an ongoing conversation, **When** the user switches between Push-to-Talk and Continuous Conversation Mode, **Then** the switch takes effect for the next turn without restarting the conversation, losing conversation history, or requiring the page to reload.
5. **Given** the user has previously set a conversation mode, mute state, or voice preference, **When** they return in a new session, **Then** those preferences are restored automatically.

---

### Edge Cases

- What happens when the user denies microphone permission, or permission was previously denied at the browser/OS level? The system must show a clear, visible "permission required" state with guidance to grant access, never a silently non-functional microphone button.
- What happens when the user's device has no working microphone, or the browser doesn't support the required audio capture capabilities? The system must disable voice input with a clear explanation, while leaving the rest of the chat experience fully usable.
- What happens when the voice provider is unreachable, rate-limited, or returns an error mid-session (outage, network drop)? Per Clarifications: the system automatically switches the current session to the legacy browser-based voice implementation, visibly notifies the user that voice quality is temporarily reduced, and continues the conversation without requiring a restart. If the legacy implementation is also unavailable (e.g., the browser lacks required capabilities), the system shows a clear, visible error with retry/exit options rather than failing silently.
- What happens when the primary voice provider becomes healthy again while a session is running on the legacy fallback? Per Clarifications: the system automatically retries the primary provider and transparently switches the session back to it before the next voice turn, without requiring the user to restart the conversation or take any action.
- What happens when background noise or a brief cough triggers false "user is speaking" detection in Continuous Conversation Mode? The system should tolerate brief non-speech noise without prematurely interrupting the AI or triggering an unwanted turn.
- What happens when the user stays silent for an extended period in Continuous Conversation Mode? The system must keep listening without erroring or timing out the conversation, remaining ready to react whenever the user does speak.
- What happens if the user switches conversation modes while a voice turn (listening, processing, or AI speaking) is already in progress? The in-progress turn must complete or be cleanly cancelled — never left in an inconsistent or stuck state — before the new mode takes effect.
- What happens if the browser tab loses focus or is backgrounded while Continuous Conversation Mode is active? The system must not leave the microphone silently capturing in a way the user can't see is happening, and must resume cleanly when the tab regains focus.
- What happens when the user tries to send a typed text message while a voice turn is in progress? Both input paths must not conflict — the system must handle whichever input completes, without corrupting conversation order or losing content.
- What happens when transcription confidence is low or the transcript is empty (e.g., mumbled or inaudible speech)? The user must see a clear indication that nothing usable was captured, rather than an empty or nonsensical AI reply being generated from blank input.
- What happens when a user's saved default voice, device, or preference is no longer available (e.g., a previously selected microphone is unplugged)? The system must fall back to a working default with a visible notice, not fail silently or crash the interaction.

## Requirements *(mandatory)*

### Functional Requirements

**Speech-to-Text**

- **FR-001**: System MUST capture the user's spoken input and convert it to text using a streaming speech-to-text engine, producing a usable transcript without requiring the entire utterance to finish before transcription begins.
- **FR-002**: System MUST automatically detect when the user has stopped speaking (end-of-speech / silence detection) and use that to trigger processing, without requiring a manual "done speaking" action, while still allowing the user to end capture manually if they choose.
- **FR-003**: System MUST request and clearly handle microphone permission, presenting a distinct, visible state when permission is required, denied, or unavailable.
- **FR-004**: System MUST automatically attempt to recover from a transient loss of connection to the speech-to-text engine during an active listening session, without requiring the user to restart the entire voice interaction, and MUST surface a visible error if recovery is not possible.
- **FR-005**: System MUST tolerate normal background noise without repeatedly misfiring speech-start/speech-end detection during ordinary use.
- **FR-006**: Users MUST be able to cancel an in-progress speech capture before it is processed.
- **FR-007**: System MUST support speech-to-text in every language the product's user interface currently supports (per Clarifications) — no narrower language coverage than the system being replaced.

**Text-to-Speech**

- **FR-008**: System MUST convert the AI's text response into natural-sounding speech using a streaming synthesis engine, and MUST begin audio playback as soon as the first portion of speech is available rather than waiting for the full response to be synthesized.
- **FR-009**: System MUST produce a single, consistent voice persona (matching the product's existing young-adult-female voice requirement) across every supported language, rather than a different or inconsistent voice per language. Per Clarifications, this persona consistency requirement applies equally when a session is running on the legacy fallback implementation (see Reliability & Error Handling) — a fallback voice mismatched to the persona is not acceptable, even though other aspects of voice quality may be temporarily reduced.
- **FR-010**: Users MUST be able to select from a set of available voices and adjust voice characteristics (at minimum: speaking speed and expressive style), with their choice applied to future replies.
- **FR-011**: System MUST support immediately cancelling in-progress speech synthesis and playback (see Stop and Interruption requirements below), with no further audio from a cancelled reply produced afterward.
- **FR-012**: System MUST support speech synthesis in every language the product's user interface currently supports (per Clarifications).

**Conversation Modes**

- **FR-013**: System MUST support a Push-to-Talk mode in which the microphone is inactive until explicitly activated by the user, and returns to inactive after each exchange completes.
- **FR-014**: System MUST support a Continuous Conversation Mode in which the microphone remains active, the system automatically detects when the user starts speaking, and listening automatically resumes after the AI finishes speaking — without requiring a click between turns.
- **FR-015**: Users MUST be able to switch between Push-to-Talk and Continuous Conversation Mode at any time, and the switch MUST take effect without restarting the conversation or losing conversation history/context.
- **FR-016**: System MUST persist the user's selected conversation mode and restore it automatically in future sessions.

**Natural Turn-Taking & Interruption**

- **FR-017**: System MUST detect when the user begins speaking while the AI is speaking and immediately stop AI audio playback and any further generation/synthesis of that reply. This MAY resolve in two steps rather than one atomic action: audible playback MUST become inaudible to the user immediately on the earliest available local signal, while full cancellation of generation/synthesis MAY complete slightly after, once the interruption is confirmed — provided that confirmation happens fast enough to meet SC-002, and provided a false-detection (e.g., a brief non-speech noise) can resume the paused playback rather than needing to restart the reply from scratch.
- **FR-018**: Upon interruption, System MUST immediately begin capturing the user's new speech, without requiring a separate manual stop action first.
- **FR-019**: System MUST NOT resume or replay an interrupted AI reply after the user has moved on to a new message.

**Voice Controls**

- **FR-020**: System MUST display distinct, visually identifiable states for the microphone at minimum: idle, listening, user speaking, processing, AI speaking, disabled, and permission-required.
- **FR-021**: Users MUST be able to mute audio output at any time; while muted, the AI MUST continue generating its response and the voice-reactive visualization MUST continue reacting to the generated audio — only the audible output is suppressed.
- **FR-022**: Users MUST be able to unmute audio output, after which subsequent replies play normally; a reply that played (or was generated) while muted is not retroactively played back.
- **FR-023**: Users MUST be able to immediately stop an in-progress AI reply; stopping MUST cancel playback, cancel further generation/synthesis of that reply, clear any queued audio, reset the visualization to its idle behavior, and resume listening automatically if Continuous Conversation Mode is active.
- **FR-024**: All voice controls (microphone toggle, mode switch, mute/unmute, stop) MUST be operable via keyboard, in addition to pointer/touch interaction.

**Visualization Integration**

- **FR-025**: The existing 3D voice-reactive sphere visualization MUST react to the actual audio being played back for the AI's spoken reply, rather than an approximated or simulated signal.
- **FR-026**: The visualization MUST begin reacting to AI speech as soon as playback begins (i.e., as the first audio arrives), not only once the full reply has finished synthesizing.
- **FR-027**: When AI speech is not playing, the visualization MUST continue its existing idle behavior; when speech starts and stops, the transition between idle and reactive behavior MUST appear smooth rather than abrupt.
- **FR-028**: This feature MUST NOT alter the sphere's existing visual design, rendering technique, or idle animation — only the source of the data driving its reactivity changes.

**Preferences**

- **FR-029**: System MUST persist, per user, their conversation mode, mute state, selected voice, voice speed/style, and selected microphone device, and restore these automatically on the user's next visit.
- **FR-030**: System MUST persist the user's selected speaker/output device where the platform the user is on supports choosing one.
- **FR-031**: If a previously saved preference (e.g., a specific microphone) is no longer available, System MUST fall back to a working default and visibly notify the user, rather than failing silently.

**Reliability & Error Handling**

- **FR-032**: System MUST never leave a voice interaction in an indefinite, unexplained "stuck" state (e.g., mic shows listening with nothing happening, or AI shows speaking with no audio) — every failure MUST surface a visible, actionable message to the user.
- **FR-033**: Per Clarifications, when the primary voice provider is unreachable, rate-limited, or errors mid-session, System MUST automatically switch the active voice session to the legacy browser-based speech-to-text and text-to-speech implementation, MUST display a clear, visible notice that voice quality is temporarily reduced, and MUST continue the conversation without requiring the user to restart it.
- **FR-034**: Per Clarifications, while a session is running on the legacy fallback implementation, System MUST automatically retry the primary voice provider and, if it has recovered, transparently switch the session back to it before the user's next voice turn, without requiring the user to take any action or restart the conversation.
- **FR-035**: The legacy browser-based voice implementation MUST be kept functional and available in production indefinitely as the fallback path, not removed once the primary voice provider is validated.
- **FR-036**: If the legacy fallback implementation is also unavailable (e.g., the browser lacks required capabilities, or microphone/synthesis access fails), System MUST show a clear, visible error with the ability to retry or exit voice mode rather than failing silently.
- **FR-037**: When a session automatically switches between the primary and fallback voice implementations, in either direction, in-progress conversation history and context MUST be preserved unchanged.
- **FR-038**: Users MUST be able to fall back to typed text input at any time if voice interaction is unavailable in both the primary and fallback implementations, without losing the existing conversation.
- **FR-039**: Per Clarifications, System MUST make voice fallback activity (when and how often voice sessions switch from the primary provider to the legacy fallback) visible to administrators, mirroring the visibility already provided for other AI provider health/outage conditions, so repeated failovers can be identified as a possible primary-provider outage without a user needing to report it.

**Access & Scope**

- **FR-040**: Per Clarifications, this voice conversation capability MUST be available to all authenticated users at launch, without subscription-tier gating; usage is governed by the platform's existing request-level rate limiting rather than a new tier restriction.

**Security & Privacy**

- **FR-041**: System MUST NOT expose voice-provider credentials to the browser or any client-side code at any point.
- **FR-042**: System MUST clearly indicate to the user, at all times, whether the microphone is actively capturing audio.
- **FR-043**: Audio captured from the user MUST only be transmitted for the purpose of transcription for the active conversation and MUST NOT be retained by the system beyond what is needed to produce the transcript, consistent with the platform's data-handling principles.

### Key Entities

- **Voice Session**: An active voice-conversation instance tied to a chat conversation. Tracks the current conversation mode (Push-to-Talk / Continuous), the current voice state (see Voice State below), and start/end timing.
- **Voice Turn**: A single user-speaks / AI-responds exchange within a Voice Session. Tracks the resulting transcript, the AI's response text, whether it was interrupted, and audio duration.
- **Voice State**: The centralized state a Voice Session is in at any moment — at minimum idle, listening, user speaking, processing, AI thinking, AI speaking, interrupted, muted, and error — which every voice-related UI control reflects.
- **User Voice Preferences**: A user's persisted settings: conversation mode, mute state, selected voice, voice speed/style, selected microphone device, and selected speaker/output device.
- **Voice Persona**: The specific synthesized voice (and its per-language mapping) used for AI speech, chosen to present a single, consistent persona across all supported languages.
- **Voice Provider Status**: Tracks which voice implementation (primary or legacy fallback) is currently active for a session, whether it is active by default or as a result of an automatic failover, and whether a recovery attempt back to the primary provider is pending.
- **Voice Provider Health Signal**: An administrator-visible record of when and how often voice sessions have failed over from the primary provider to the legacy fallback, used to surface a possible primary-provider outage, mirroring the existing AI provider health-monitoring pattern.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In Push-to-Talk mode, the AI's spoken reply audibly begins playing within 2 seconds of the AI starting to generate its response, without waiting for the full response.
- **SC-002**: When the user interrupts AI speech, audio playback stops within 300 milliseconds of the user starting to speak, in at least 95% of measured interruptions.
- **SC-003**: In Continuous Conversation Mode, listening automatically resumes after the AI finishes speaking within 1 second, with zero manual clicks required, across a full multi-turn conversation.
- **SC-004**: Users can complete an entire multi-turn spoken conversation (at least 3 back-and-forth exchanges) using only their voice, with no required mouse or keyboard interaction beyond entering and exiting voice mode.
- **SC-005**: When the primary voice provider becomes unreachable, 100% of affected voice sessions automatically continue via the legacy fallback implementation within 3 seconds, with a visible notice shown to the user — none are left in a silent or indefinitely "processing" state, and none end the conversation outright unless the fallback is also unavailable.
- **SC-006**: In a usability test, at least 90% of participants describe the conversational voice experience as feeling natural or comparable to talking with a person, up from the current browser-voice baseline.
- **SC-007**: Muting audio output takes effect within 200 milliseconds and never interrupts the AI's in-progress response generation or the visualization's reaction to it.
- **SC-008**: A user's conversation mode, mute state, and voice preferences persist across 100% of returning sessions without needing to be reconfigured.
- **SC-009**: Voice interaction is fully usable via keyboard alone (activating the microphone, muting, stopping, and switching modes), verified with no pointer input during testing.
- **SC-010**: A voice session running on the fallback engine automatically returns to the primary provider on the next voice turn after the primary recovers, with no user action required, in 100% of observed recoveries.
- **SC-011**: An administrator can detect that voice sessions are failing over to the fallback engine without being told by a user, using the same reporting/monitoring surface already used for other AI provider health conditions.

## Assumptions

- **Server-side credential handling.** Consistent with this project's security principles (secrets are never exposed to the browser), all calls to the voice provider are made from the backend rather than directly from client-side code; the browser only exchanges audio/transcript/response data with the platform's own backend. The voice provider's API credential is configured as a server-side secret (e.g., environment variable / secret store), never committed to source control or embedded in client code — no credential value is recorded in this specification or its supporting documents.
- **Legacy voice path is retained permanently as an automatic, bidirectional fallback, not retired.** (Revised 2026-08-02, see Clarifications.) Unlike a typical full-replacement migration, the existing browser-based transcription and speech-synthesis implementation remains in production indefinitely as the automatic degraded-mode backup for whenever the primary voice provider is unreachable, and sessions automatically recover back to the primary provider once it's healthy again (FR-034). Both implementations must therefore stay functional, tested, and maintained going forward, and fallback activity must be visible to administrators (FR-039) — this expands ongoing scope beyond a one-time swap, and the two-implementation switching behavior itself (not just the primary engine) is now part of what this feature must deliver.
- **Visualization gets real audio data, not just a preserved approximation.** The sphere visualization currently reacts to an approximated signal derived from speech-timing events, not the actual synthesized audio. This specification intentionally upgrades that to real, audio-derived reactivity (FR-025) as part of the migration — the sphere's visual design, rendering technique, and idle behavior are unchanged (FR-028); only the realism of what drives its reactivity improves.
- **Voice persona continuity, including in fallback mode.** (Revised 2026-08-02, see Clarifications.) The product's existing requirement for a single, consistent, young-adult-female-sounding voice across languages carries forward unchanged and applies whether the primary or legacy fallback implementation is active (FR-009); the primary voice satisfies it using the new provider's voice/language capabilities, while the fallback continues to use the legacy engine's existing per-browser voice-matching approach.
- **Text chat remains available as a fallback.** Voice is an alternate input/output mode for the existing chat experience, not a replacement for typed conversation; a user can always fall back to text if voice is unavailable in both the primary and legacy implementations (FR-038).
- **Underlying conversation/session infrastructure is reused.** This feature adds a voice layer on top of the existing conversation history, message persistence, and AI response generation; it does not change how conversations or messages themselves are stored or attributed.
- **Provider abstraction is a stated goal, not a hard requirement to ship multiple providers now.** The feature description calls for "future support for additional speech providers"; this specification requires only that today's single provider be usable in production, with the underlying design decision of whether/how to abstract for future providers left to the planning phase.
- **Reasonable defaults apply to unspecified operational details** such as exact reconnect retry counts, specific noise-tolerance thresholds, and exact silence-timeout duration for end-of-speech detection — these are tuned during implementation against the measurable outcomes in Success Criteria rather than fixed numerically in this specification.
- **Voice endpoint rate limiting is not newly introduced by this spec.** The `/api/v1/ai/voice/*` endpoints introduced by this feature are covered by the platform's existing baseline per-user/tenant request-count rate limiting; this feature does not introduce additional token- or cost-based throttling for voice sessions specifically. Token/cost-based throttling for AI-invoking endpoints generally — voice included — is deferred to the future Billing Engine specification, consistent with specs/005-multi-provider-ai-engine's identical Assumption for its own AI-invoking endpoints.
