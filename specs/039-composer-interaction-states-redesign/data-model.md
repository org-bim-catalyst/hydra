# Phase 1 Data Model: Composer Interaction States Redesign

This feature has no persisted/backend data model changes (research.md Decision 3, 6). The
"entities" here are client-side UI/state shapes — derived from spec.md's Key Entities
section and grounded in the actual TypeScript types already present in
`src/AskLucy.Web/ClientApp/src/features/chat/`. Types are illustrative of the *shape*
required; exact naming is a `/speckit-tasks` + implementation concern.

## Composer State

Maps to spec.md's "Composer State" entity. Not a single new discriminated-union type to
introduce from scratch — it is the *derived* combination of state already tracked across
several existing sources, which the components below already read individually:

| Source of truth | Field | Drives |
|---|---|---|
| `ChatComposer`'s local `value` prop (lifted from `ChatPage`) | `value: string` | empty vs. typing (FR-001–FR-004) |
| `recording?.phase` (`RecordingPhase`: `'idle' \| 'recording' \| 'reviewing' \| 'transcribing'`) + local `isAwaitingTapReview` | click-to-talk vs. hold-to-talk visual branch (FR-005–FR-011) | |
| `voicePreferencesStore().conversationMode` (`'PushToTalk' \| 'Continuous'`) + `isListening` | continuous-conversation idle-listening vs. typing-while-listening (FR-012–FR-017) | |

**Validation rules** (from Requirements):
- `value` non-empty ⟺ send action enabled (FR-003).
- `recording.phase !== 'idle'` ⟹ attach/mic-idle/continuous-conversation controls hidden
  (already the case today; FR-005/FR-009 extend this to the redesigned icon set).
- `conversationMode === 'Continuous' && isListening` ⟹ mute/exit controls shown, not
  attach/mic/continuous-conversation-entry (FR-012).

No new enum/type is needed to *store* this — it is presentational logic branching on
existing state. If implementation finds the branching (empty / typing / click-review /
hold-active / continuous-idle / continuous-typing) awkward as ad hoc conditionals, a local
derived `type ComposerVisualState = 'empty' | 'typing' | 'click-review' | 'hold-active' |
'continuous-idle' | 'continuous-typing'` computed via a pure function from the above sources
is an acceptable internal refactor — but it is not a new *persisted* entity.

## Voice Recording Session

Already fully modeled by existing types — no changes:

```ts
// src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoiceRecorder.ts (unchanged)
type RecordingPhase = 'idle' | 'recording' | 'reviewing' | 'transcribing'
```

`'reviewing'` is not currently reachable via the redesigned flows described in spec.md (the
tap/click-to-talk path goes `recording` → user clicks Finish → `transcribing` directly, per
`RecordingReviewControls.tsx`'s existing `onFinish` wiring) — confirm during implementation
that no dead branch needs updating, but no *new* phase is required for this feature.

## Continuous Conversation Session

Already modeled by `voicePreferencesStore` (persisted) + `ChatComposer`'s `isListening` prop
(session-transient, not persisted) — no schema change. The only behavioral addition (research.md
Decision 3) is that entering Continuous mode via the composer action must, as one action,
call *both* `voicePreferencesStore.update({ conversationMode: 'Continuous' })` *and*
`onStartCapture()`, and exiting must call *both* the inverse update *and* `onStopCapture()` —
today these two calls are only ever triggered independently (mode-switch button vs. mic
button), never paired.

**Ordering/failure rule** (analysis remediation C1): `update()` is async and rolls back its
own state with a visible `error` on save failure (`voicePreferencesStore.ts`'s existing
try/catch). The two calls are **not** symmetric:
- **Entering** Continuous MUST `await update({ conversationMode: 'Continuous' })` before
  calling `onStartCapture()` — if the save rejects/rolls back, capture must never start
  against a preference that didn't actually persist as Continuous.
- **Exiting** Continuous MUST call `onStopCapture()` immediately/synchronously — listening
  stops right away regardless of the save's outcome — then `await
  update({ conversationMode: 'PushToTalk' })` for its own (pre-existing) error surfacing if
  the save fails. Stopping capture is never worth delaying behind a network round-trip.

```ts
// voicePreferencesStore.ts (unchanged shape)
interface UserVoicePreference {
  conversationMode: 'PushToTalk' | 'Continuous'
  isMuted: boolean
  // ...unrelated fields unchanged
}
```

## Assistant Reply Playback

**New** transient (non-persisted) state, owned by `ChatPage.tsx` (the same component that
already owns the single shared `useVoiceOutput()` instance), not by `MessageBubble` itself
(a `MessageBubble` is one of many rendered by a virtualized list — `useVirtualizer` — and
must not each hold independent "am I the one playing" state that could desync from the
single shared audio channel).

```ts
// ChatPage.tsx — new local state (research.md Decision 4/5)
const [playingMessageId, setPlayingMessageId] = useState<string | null>(null)
// analysis remediation F1 — distinguishes an auto-spoken reply (disabled+play, FR-021)
// from a user-initiated replay of that same message (enabled+stop, FR-022/FR-024). Without
// this, a single isPlaying flag cannot tell the two apart and would render an interactive
// Stop button during every auto-spoken reply, contradicting FR-021's "disabled ... while
// speaking that reply for the first time."
const [isManualReplay, setIsManualReplay] = useState(false)

// Derived per MessageBubble (passed down as props, not looked up independently):
interface ReplayControlProps {
  /** True only when THIS message is playing AND it was a user-initiated replay — the sole
   * condition for showing the interactive Stop control (FR-022/FR-024). False for a message
   * currently auto-speaking for the first time, even though it "is playing." */
  showStopIcon: boolean
  /** Disabled per FR-021 (muted, or this exact message auto-speaking) and per FR-023 (some
   * other message currently playing) and per Edge Case 5 / Assumptions (analysis remediation
   * F2: a voice-recording or continuous-listening session is currently active) — irrelevant
   * when showStopIcon is true, since Stop is always enabled (FR-024). */
  isReplayDisabled: boolean
  onReplay: () => void   // FR-022/FR-025: always starts from the beginning
  onStop: () => void     // FR-024
}
```

**Derivation** (owned by `ChatPage.tsx`, passed down — not computed inside `MessageBubble`):

```ts
const isThisMessagePlaying = message.id === playingMessageId
const showStopIcon = isThisMessagePlaying && isManualReplay

const isRecordingOrListeningActive =
  (recording !== undefined && recording.phase !== 'idle') || isListening

const isReplayDisabled =
  isMutedPreference ||
  !message.id ||
  isRecordingOrListeningActive ||                  // F2
  (isThisMessagePlaying && !isManualReplay)         // F1 — disabled only while THIS message
                                                      // is auto-speaking itself; does NOT
                                                      // disable other, non-playing replies —
                                                      // see the post-implementation
                                                      // correctness fix below
```

**Post-implementation correctness fix**: an earlier revision of this formula additionally
disabled every *other* reply whenever *any* reply was playing
(`playingMessageId !== null && !showStopIcon`). That directly contradicted FR-023 ("starting
playback/replay on one reply MUST stop any other reply currently playing") — a requirement
that is unreachable if the button for that "other reply" is disabled the moment something
starts playing. This slipped past three `/speckit-analyze` rounds because none of them
traced a concrete two-message click sequence through the formula; it was caught by writing
the actual `ChatPage.tsx` integration test for FR-023 during implementation. The corrected
formula above disables a reply's own control only for its own FR-021 auto-speak case (plus
the unconditional mute/recording-listening/no-id cases) — clicking a different, enabled
reply's Replay while another plays is what triggers `handleReplay`'s existing
"`tts.stop()` if `tts.isSpeaking`, then `tts.speak()`" sequence (contracts/reply-playback-
control.md), which was always correct — only the *disabled* gate was over-broad.

**State transitions** (FR-020–FR-025):

```text
[not playing] --replay clicked (enabled, showStopIcon false)--> [this message playing, showStopIcon true]
[this message playing, showStopIcon true] --stop clicked--> [not playing]
[this message playing, showStopIcon true] --replay clicked on a DIFFERENT message-->
    stop this (tts.stop()), then [other message playing, showStopIcon true for that one]
[any message playing] --playback ends naturally (TTS onend)--> [not playing]
[not playing] --auto-speak fires for a new reply--> [new message playing, isManualReplay=false,
                                                       so showStopIcon FALSE — that reply's own
                                                       control stays disabled+play, per FR-021]
                                                      (stops any prior replay first — Decision 5)
```

**Validation rules**:
- At most one `playingMessageId` value at any time (FR-023) — enforced by `ChatPage.tsx`
  always calling `tts.stop()` before `tts.speak()` for a different target inside
  `handleReplay` itself, never by disabling every other reply's button (see the corrected
  `isReplayDisabled` formula above — that would make FR-023's "switch" scenario unreachable).
- **Reverse direction** (analysis remediation E4, symmetric to F2): starting a new recording
  or continuous-listening session MUST first call `handleStopReplay()` if `showStopIcon` is
  currently `true` for any message — the same "one audio subsystem" reasoning F2 applied to
  disabling replay during recording/listening applies in reverse: a manual replay must not
  keep speaking (and risk being captured by the microphone) once the user starts recording or
  listening. This is implemented by wrapping the existing `onStartCapture` pass-through in
  `ChatPage.tsx` (the same function already threaded to `ChatComposer`, unchanged by this
  feature otherwise), not by adding a new mechanism.
- `isReplayDisabled` is `true` whenever `voicePreferencesStore().isMuted` is `true`,
  regardless of `playingMessageId` (FR-021), **and** whenever a recording/listening session
  is active (F2), **and** for the message currently being auto-spoken for the first time
  (F1) — but never gates the Stop control itself, which is unconditionally enabled per FR-024
  whenever `showStopIcon` is true.
- Restarting (`onReplay` after a stop) is always a fresh `tts.speak(message.content,
  language)` call — `useVoiceOutput`/`useTextToSpeech` have no resume/seek capability, so
  "restart from the beginning" (FR-025) is the natural behavior, not something requiring
  extra logic to enforce.

## Key Entity Cross-Reference

| spec.md Key Entity | Concrete implementation location |
|---|---|
| Composer State | Derived from `ChatComposer` props + `voicePreferencesStore` (no new type) |
| Voice Recording Session | `RecordingPhase` (`useVoiceRecorder.ts`, unchanged) |
| Continuous Conversation Session | `UserVoicePreference.conversationMode` + `isListening` (unchanged shape, new paired-call behavior) |
| Assistant Reply Playback | New `playingMessageId` + `isManualReplay` state in `ChatPage.tsx` + extended `ReplayControlProps` into `MessageBubble.tsx` |
