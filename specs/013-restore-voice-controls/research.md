# Research: Restore Voice Output Mute & Input Mode Controls

## Decision 1 — Reuse existing building blocks, but compose them narrower than the full `useConversationAudio` session

**Decision**: Reuse `useSpeechRecognition` (input capture), `useVoiceAnalyzer`'s mute
mechanism via an extended `useVoiceOutput` (output), `voicePreferencesStore` (persistence),
and `VoiceControlBar`'s markup/a11y pattern (UI shell, with an adapted prop contract — see
Decision 2). **Do not** adopt `useConversationAudio` wholesale as the orchestrator.

**Why not `useConversationAudio` wholesale (correction from initial planning pass)**:
`useConversationAudio` hardwires "a voice-captured transcript triggers an LLM turn, which
is then spoken" — `runAssistantTurn` is only ever invoked from `handleFinalTranscript`,
which only fires from mic input. `ChatPage.tsx`'s current live behavior (the
`wasStreamingRef` effect, restoring spec **000**'s legacy FR-006) speaks **every** completed
assistant reply aloud, regardless of whether the user typed it or spoke it. Swapping in
`useConversationAudio` as-is would silently narrow "every reply is spoken" down to "only
replies to voice-initiated turns are spoken" — a real regression of already-shipped,
already-relied-upon behavior that nothing in spec 013 asked for. Mute (US1) and input mode
(US2) are requirements about the *output* surface and the *input* surface respectively;
they do not require coupling one to the other the way `useConversationAudio` does.

**Revised composition**:
- **Output/mute (US1)**: extend `useVoiceOutput` (the hook `ChatPage.tsx` already uses for
  its every-reply auto-speak effect) with mute awareness, sourced from
  `voicePreferencesStore.isMuted`. The auto-speak effect itself is **kept**, not removed —
  it still fires for every completed reply, typed or spoken. See Decision 3.
- **Input/mode (US2)**: use `useSpeechRecognition` directly in a new mic control that
  replaces `ChatComposer`'s one-shot `useWavRecorder` button. Its finalized transcript feeds
  the same `send()` path (`useChatStream`) typed messages already use — so the (now
  mute-aware) auto-speak effect uniformly handles the resulting reply's audio, whether the
  turn started as typed or spoken text. See Decision 4.
- **UI shell**: `VoiceControlBar.tsx`'s existing markup, icons, tooltips, and keyboard-a11y
  pattern (already covered by `VoiceControlBar.test.tsx`'s jest-axe check) are reused, but
  its prop contract is adapted from "reflects a `useVoiceState` conversation-turn machine"
  to "reflects `useSpeechRecognition`'s listening state + `useVoiceOutput`'s
  speaking/muted state" — see contracts/voice-control-integration.md. `useVoiceState` and
  `useConversationAudio` are not used by this feature and remain as they are (available for
  a future full conversational-mode feature, out of this scope).

**Rationale**: Satisfies constitution §18 ("reuse... don't duplicate") for the pieces that
are genuinely reusable (recognition, mute-capable analyzer, preference persistence, UI
shell) without inheriting the one piece (`useConversationAudio`'s turn orchestration) whose
built-in behavior conflicts with an existing, relied-upon requirement this feature must not
regress.

**Alternatives considered**:
- *Adopt `useConversationAudio`/`VoiceControlBar` wholesale as originally planned.*
  Rejected on discovering the every-reply-vs-voice-only-reply regression above.
- *Add mute + mode toggle to today's live path with entirely new, from-scratch logic
  (ignoring `useSpeechRecognition`/`useVoiceAnalyzer` entirely).* Rejected — would duplicate
  already-built, already-partially-production-tested capture/mute logic for no benefit.
- *Build a brand-new, third voice subsystem.* Rejected outright — no justification for new
  architecture when suitable pieces already exist.

**Supporting evidence**: `useVoiceAnalyzer.ts` — the hook whose gain-node mute mechanism
this feature draws on conceptually — is already used live by `useVoiceOutput`
(`useVoiceAnalyzer(handlePlaybackError)`), and has received two real production bug fixes
in the last day (#281, #283, both citing production console logs). The output-side
mechanism this feature extends is not untested dormant code. The genuinely unverified piece
is confined to `useSpeechRecognition`'s ElevenLabs realtime STT wire protocol — see
Decision 6 — and, with this narrower composition, that risk is isolated to US2 only; US1
(mute) has no dependency on it and can ship and be validated independently even if US2's
STT verification takes longer.

## Decision 2 — `VoiceControlBar`'s prop contract is adapted, not reused as-is

**Decision**: `VoiceControlBar.tsx` keeps its markup/icons/tooltips/keyboard pattern but
its props change from `{ voiceState: VoiceStateName, conversationMode, isMuted, onStart,
onCancelListening, onStop, onToggleMode, onToggleMute, onClearError }` to a smaller surface
driven by `useSpeechRecognition` + the extended `useVoiceOutput` directly (exact shape in
contracts/voice-control-integration.md) — there is no `useVoiceState` turn machine backing
it in this feature's composition.

**Rationale**: The original props existed to reflect `useConversationAudio`'s 9-state turn
machine (`Idle`/`Listening`/`UserSpeaking`/`Processing`/`AiThinking`/`AiSpeaking`/
`Interrupted`/`Muted`/`Error`). With that orchestrator not in use (Decision 1), most of
those states don't apply — input capture only ever needs `isListening`/`error`/
`permissionState` (already returned by `useSpeechRecognition`), and output only needs
`isSpeaking`/`isMuted`/`error` (already returned, or added, to `useVoiceOutput`). Keeping
the old, wider prop contract would force fabricating states that no longer mean anything.

## Decision 3 — Mute mechanism: gate `speak()` while muted, `stop()` on mute-while-playing

**Decision**: `useVoiceOutput` gains `isMuted` (from `voicePreferencesStore.isMuted`, via
an effect keeping it in sync) and a `toggleMute`/`setMuted` action that:
1. Immediately calls the hook's existing `stop()` if a reply is currently playing when
   mute is turned on — stopping audible playback right away (SC-001, Acceptance Scenario 1).
2. Makes `speak()` a no-op while `isMuted` is true — a reply completed while muted is never
   queued or started (Acceptance Scenario 2), so there is nothing left to become audible
   later.
3. On unmute, does nothing retroactively — only the *next* call to `speak()` (the next
   completed reply) plays, matching Clarification Q2 exactly ("only future replies speak").

**Rationale**: This is simpler than repurposing `useVoiceAnalyzer`'s gain-node suppression
for this call site (gain-node muting would leave a reply's audio element silently
finishing playback in the background while muted, which risks becoming audible again if
naively unmuted mid-stream — exactly the outcome Clarification Q2 rules out). Gating at
the `speak()`/`stop()` level is a smaller change to an already-tested hook and has no such
edge case: nothing plays while muted, full stop.

**Because `speak()` is only ever called after the assistant's text reply has finished
generating** (the existing `wasStreamingRef` effect only fires on the streaming→done
transition), FR-002's "muting MUST NOT interrupt/delay reply generation" is automatically
satisfied — by the time mute can affect anything, generation is already complete.

**Alternatives considered**: Reusing `useVoiceAnalyzer.setMuted`'s gain-node approach for
this call site — rejected per the retroactive-audibility edge case above. (That mechanism
remains valid and unchanged for its original purpose inside `useConversationAudio`, which
this feature does not use.)

## Decision 4 — `ChatComposer`'s dictate button becomes a mode-aware mic control

**Decision**: `ChatComposer.tsx` drops `useWavRecorder`/`transcribeMicrophoneAudio` and
instead uses `useSpeechRecognition` directly, with behavior branching on
`voicePreferencesStore.conversationMode`:
- **Push-to-Talk**: capture starts on hold-or-toggle-press (Decision 5) and stops on
  release/second-press; the finalized transcript **fills the composer's text field** for
  the user to review/edit before sending — preserving the existing, already-shipped
  reviewable-dictation UX (`handleConfirmVoice`'s current behavior), just re-platformed
  onto `useSpeechRecognition` instead of `useWavRecorder`+`transcribeMicrophoneAudio`.
- **Continuous Conversation**: the mic stays active; each finalized transcript (auto-committed
  after a pause, per `useSpeechRecognition`'s existing `SILENCE_COMMIT_DELAY_MS` logic) is
  sent immediately via the same `send()` typed messages use, without requiring a manual
  click — this is what makes the mode hands-free (FR-006), and mirrors Clarification Q3
  (typing in the composer does not pause or stop continuous listening, since the two paths
  — typed `send()` calls and voice-triggered `send()` calls — are already independent
  once both go through the same `send()` function).

**Rationale**: Keeps a single, coherent mic surface (satisfies FR-004) while reusing
`useSpeechRecognition`'s already-built continuous-mode silence-detection instead of
reinventing it, and preserves the existing "review before send" UX that push-to-talk users
already rely on today rather than silently changing it to auto-send.

**Alternatives considered**: Auto-send in both modes — rejected, would change existing
push-to-talk-style dictation behavior (today's reviewable flow) without the spec asking
for that; reviewability is exactly what distinguishes "push-to-talk" from "continuous" in
the clarified spec.

## Decision 5 — Push-to-talk activation: hold and toggle both wired to the same control

**Decision**: The mic control gets `onPointerDown`/`onPointerUp` (and
`onTouchStart`/`onTouchEnd`) handlers that call `useSpeechRecognition.start`/`stop`
directly (true hold), in addition to a `onClick` toggle path (press once to start, again
to stop) for users who tap/click instead of holding. A bound key's `keydown`/`keyup`
(matching spec 012's original "Space = hold-to-talk" task note) drives the same hold path
for keyboard users.

**Rationale**: Matches the Clarifications answer exactly ("Both hold and toggle
supported") and satisfies FR-010's keyboard-operability requirement via one shared code
path rather than a second, separate keyboard-only implementation.

**Alternatives considered**: Hold-only and toggle-only were both explicitly rejected by
the clarification answer; not reconsidered here.

**Residual risk flagged for implementation**: An element that fires both
`pointerdown`/`pointerup` and a synthetic `click` needs de-duplication (a naive
implementation would toggle a second time on release) — call this out explicitly in
tasks.md so it isn't missed; it is a well-understood UI-implementation detail, not a
design ambiguity, and does not block this plan.

## Decision 6 — Mode-switch guard during an active push-to-talk capture

**Decision**: The mode toggle control becomes disabled (with a `Tooltip` explaining why)
whenever `conversationMode === 'PushToTalk' && recognition.isListening` — i.e., while a
push-to-talk capture (started by either hold or toggle) is actively in progress. It
re-enables the instant `isListening` goes false (capture released/finalized).

**Rationale**: Matches the Clarifications answer ("Block the switch until released").
`useSpeechRecognition.isListening` (already returned today) is sufficient to derive this
guard — no new state is needed.

**Alternatives considered**: "finish and send, then switch" and "discard and switch
immediately" were both explicitly rejected by the clarification answer.

## Decision 7 — Sphere visualization intensity source

**Decision**: `SceneBackground`'s reactive sphere keeps reading intensity from
`useVoiceOutput.getIntensity` (`ChatPage.tsx` line ~71, unchanged) — since output/speaking
continues to flow entirely through `useVoiceOutput` (Decision 1/3), no second intensity
source needs to be composed in. The sphere reacts to whatever `useVoiceOutput` is playing,
whether triggered by the auto-speak effect or the Translate button, exactly as it does
today.

**Rationale**: This follow-up concern from the original (rejected) `useConversationAudio`-based
plan no longer applies once output stays entirely on `useVoiceOutput` — noted here so it
isn't mistakenly re-introduced as a task.

## Decision 8 — ElevenLabs realtime STT wire-protocol verification (residual risk, scoped to US2 only)

**Observation**: `useSpeechRecognition.ts`'s own doc comment flags that the exact JSON
message shapes (`audio_chunk`, `partial_transcript`, `committed_transcript`) it speaks to
ElevenLabs' realtime STT WebSocket were a "residual verification item" from spec 012's
research — never confirmed against production documentation before this code went unused.

**Decision**: This plan does not re-verify the wire protocol itself (that is an
implementation task) but carries the risk forward explicitly. `tasks.md` MUST include a
task, early in the US2 phase, to verify/fix the realtime STT message contract against
current ElevenLabs documentation — both push-to-talk and continuous listening depend on it
end-to-end. Because of Decision 1's narrower composition, this risk no longer threatens
US1 (mute) at all — US1 can be implemented, tested, and shipped independently of this
verification.

**Rationale**: Constitution §2.VIII (No Silent Failures) and §18 (resolve ambiguity by
verifying/asking, not guessing) both argue against quietly shipping unverified integration
code as if it were known-good.

**Addendum (production incident, post-implementation)**: this decision scoped verification to
the realtime WebSocket message shapes only. A *sibling* unverified item — the upstream
ElevenLabs REST endpoint the backend calls to mint the session token in the first place
(`ElevenLabsSpeechToTextSessionProvider.cs`, spec 012) — was missed by that scoping and broke
in production the first time a live user exercised push-to-talk (tasks.md T023): the backend
was posting to a guessed, never-verified path that 404'd against every real ElevenLabs call.
Fixed and covered by a regression test; see T023 for the full incident record. Lesson for any
future similar verification task: scope it to the *entire* call chain (REST token mint +
WebSocket protocol), not just the piece explicitly named in the task description.

## Decision 9 — The Settings > Voice tab already has working persistence UI for both preferences; the gap is that neither has a live effect

**Observation**: `SettingsPage.tsx`'s `VoiceTab` component (also delivered by spec 012)
already renders a "Conversation mode" dropdown and a "Mute voice output" switch, both bound
to `voicePreferencesStore` via `update()`, and already calls `hydrateFromServer()` on mount.
This UI round-trips to the backend correctly today. The gap this feature closes is that
**neither setting currently has any live effect**: `useVoiceOutput` never reads `isMuted`,
and `ChatComposer`'s mic button never reads `conversationMode` — changing either setting in
Settings today silently does nothing to actual chat behavior.

**Decision**: This feature (a) makes `useVoiceOutput`/`ChatComposer` actually read and
react to these existing, already-working persisted settings (Decisions 3–4), and (b) adds
a quick-access mute button + mode toggle directly in the chat view via `VoiceControlBar`
(Decision 2), because FR-001's "a persistently visible control" and Acceptance Scenario 1's
"mute within ~1 second while Lucy is speaking" are not satisfiable by a Settings-page-only
toggle that requires navigating away from the conversation. The Settings tab is not
duplicated or removed — it remains the detailed configuration surface (it also covers
voice/speed/style/device selection, out of this feature's scope); `VoiceControlBar` becomes
a second, faster-access surface for the same two underlying preferences, both reading and
writing the same `voicePreferencesStore`.

**Consequence for hydration (Foundational, not story-specific)**: `voicePreferencesStore`
is currently only hydrated from the server when the user visits Settings. `ChatPage.tsx`
must also call `hydrateFromServer()` on mount so a user who never opens Settings still gets
their persisted mute/mode restored automatically (FR-011/SC-004) — this is shared
groundwork both US1 and US2 depend on, so it belongs in the Foundational phase, not either
story's phase.
