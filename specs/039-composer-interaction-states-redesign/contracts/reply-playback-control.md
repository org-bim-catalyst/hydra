# Contract: Assistant Reply Replay/Stop Control

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)
| Implements FR-020–FR-026 (User Story 5).

New surface — no prior spec covers per-message replay. Extends the existing shared
`useVoiceOutput()` instance (`ChatPage.tsx`) rather than introducing a parallel audio path
(research.md Decision 4). Types are illustrative, not final implementation code.

## `useVoiceOutput` (extended)

```ts
// src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoiceOutput.ts
// speak()/stop()/isSpeaking's existing signatures and ElevenLabs/fallback/error internals
// are UNCHANGED. ChatPage.tsx is the only caller and already owns enough state (see below)
// to implement "which message is playing" without changing this hook's public shape at all
// — listed here for completeness, not because the hook itself must change.
```

**Contract guarantee**: `useVoiceOutput` requires **no signature change**. `ChatPage.tsx`
already has the information needed: it knows which message it is calling `speak()` for at
each call site (the auto-speak effect already knows `last.id`; a new replay handler knows
the clicked message's `id`). The hook's existing single `isSpeaking` boolean plus a new
page-level `playingMessageId` (data-model.md) together fully answer "is *this* message
playing" for every `MessageBubble`.

## `ChatPage.tsx` (owner of playback-target state)

```ts
const [playingMessageId, setPlayingMessageId] = useState<string | null>(null)
// analysis remediation F1 — see data-model.md "Assistant Reply Playback". Distinguishes
// auto-spoken (disabled+play) from manually-replayed (enabled+stop) for the same message id.
const [isManualReplay, setIsManualReplay] = useState(false)

// Auto-speak effect (existing, MODIFIED): also sets playingMessageId, explicitly NOT manual
useEffect(() => {
  if (wasStreamingRef.current && !isStreaming) {
    const last = messages[messages.length - 1]
    if (last?.role === 'assistant' && last.content && last.id) {
      tts.speak(last.content, language)
      setPlayingMessageId(last.id)
      setIsManualReplay(false)   // F1 — this reply's own control must stay disabled+play
    }
  }
  wasStreamingRef.current = isStreaming
}, [isStreaming, messages, language, tts, expanded, markUnread])

// NEW: replay handler, passed down to each MessageBubble
const handleReplay = useCallback((message: ChatMessage) => {
  if (tts.isSpeaking) tts.stop()          // FR-023: stop whatever is currently playing first
  tts.speak(message.content, language)
  setPlayingMessageId(message.id ?? null)
  setIsManualReplay(true)                 // F1 — this click is what earns the Stop control
}, [tts, language])

// NEW: stop handler
const handleStopReplay = useCallback(() => {
  tts.stop()
  setPlayingMessageId(null)
  setIsManualReplay(false)
}, [tts])

// NEW: clear playback-target state when playback ends naturally (tts.isSpeaking false→true→false)
useEffect(() => {
  if (!tts.isSpeaking) {
    setPlayingMessageId(null)
    setIsManualReplay(false)
  }
}, [tts.isSpeaking])

// NEW (analysis remediation E4): the existing onStartCapture pass-through to ChatComposer,
// wrapped so starting any recording/listening session stops an in-progress manual replay
// first — symmetric to F2's "replay disabled while recording/listening" in the other
// direction. `rawOnStartCapture` is whatever this component already called onStartCapture
// before this feature; only the wrapping is new.
const handleStartCapture = useCallback(() => {
  if (playingMessageId !== null && isManualReplay) handleStopReplay()
  rawOnStartCapture()
}, [playingMessageId, isManualReplay, handleStopReplay, rawOnStartCapture])
```

**Contract guarantees**:

- `handleReplay` always calls `tts.stop()` before `tts.speak()` when something is already
  speaking — satisfies FR-023/FR-025 (stop-then-restart-from-beginning is `useVoiceOutput`'s
  only mode of operation; there is no seek/resume API to accidentally use instead).
- `playingMessageId`/`isManualReplay` are cleared whenever `tts.isSpeaking` transitions to
  `false` — covering natural completion, an explicit stop, and a playback error
  (`useVoiceOutput`'s `finally` block already sets `isSpeaking` false on error) — so no
  `MessageBubble` can be left showing a stale "stop" icon for a reply that already finished or
  failed (FR-026/analysis remediation E2: reply control must return to a defined, non-stuck
  state on failure, no less than the composer must).
- `isManualReplay` is `false` for every auto-speak call and `true` for every `handleReplay`
  call — this is the only place the distinction is set, so it can never drift out of sync with
  which action actually triggered the current playback (analysis remediation F1).
- `handleStartCapture` (analysis remediation E4) stops an in-progress manual replay before
  delegating to the pre-existing `onStartCapture` — this is the prop `ChatComposer` already
  receives and calls from its click-to-talk/hold-to-talk entry points (unchanged by this
  feature), so wrapping it here at the single pass-through point covers both without touching
  `ChatComposer.tsx` itself or duplicating the guard per entry point. Auto-speak is
  intentionally excluded from this guard — an auto-spoken reply is never the thing a user is
  about to interrupt by starting a recording; only a replay they themselves started is.
- **Round-3 finding F5**: continuous-conversation entry is a *third* path that also starts
  capture, but it's triggered from `ChatPage.tsx`'s own `onToggleMode` handler
  (contracts/composer-voice-states.md), not from `ChatComposer` calling the `onStartCapture`
  prop directly. That handler MUST call this same `handleStartCapture` function — not a
  separate raw capture-start reference — for the guard above to cover it too. This is a
  same-file, same-component coordination point between two tasks (T020 for the handler, T034
  for this wrapper), not a new mechanism.

## `MessageBubble` (extended)

```ts
interface MessageBubbleProps {
  message: ChatMessage
  chatId?: string | null

  // NEW:
  /** True only when this message is the one playing AND that playback was user-initiated
   * (drives the interactive Stop control, FR-022/FR-024). False for a message currently
   * auto-speaking for the first time, even though it "is playing" (analysis remediation F1). */
  showStopIcon: boolean
  /** True when audio is muted (voicePreferencesStore().isMuted), OR a voice-recording/
   * continuous-listening session is currently active (analysis remediation F2, Edge Case 5),
   * OR this exact message is auto-speaking for the first time (playingMessageId === this
   * message's id but isManualReplay is false — FR-021), OR the message is still streaming
   * (no stable id yet — research.md Decision 7). Ignored when showStopIcon is true — Stop is
   * unconditionally enabled (FR-024). Deliberately does NOT disable a reply just because a
   * DIFFERENT reply is currently playing — FR-023 requires that clicking a different, enabled
   * reply's Replay while another plays be possible (it's what triggers the stop-old/start-new
   * sequence); an earlier revision of this contract disabled every other reply too, making
   * FR-023 unreachable — corrected post-implementation, see data-model.md. */
  isReplayDisabled: boolean
  onReplay: (message: ChatMessage) => void
  onStopReplay: () => void
}
```

**Contract guarantees**:

- Only rendered for `message.role === 'assistant' && message.id !== undefined` (research.md
  Decision 7) — user messages and the currently-streaming message never show a replay
  control.
- Positioned in the reply bubble's lower-right corner (matching Figure 8's mockup exactly —
  visual placement is derived from the image, not re-specified pixel-by-pixel here per
  spec.md's Assumptions).
- Icon is `RiPlayFill`, `disabled` when `isReplayDisabled`, when `!showStopIcon` — this is the
  state for every reply that is not currently a *user-initiated* replay, including one
  auto-speaking for the first time (FR-021: that reply's own control stays disabled+play, not
  an interactive stop). Icon is `RiStopFill`, always enabled, when `showStopIcon` is `true`
  (FR-024) — reachable only via `onReplay`, never via auto-speak.
- `onClick` calls `onReplay(message)` when showing `RiPlayFill`, or `onStopReplay()` when
  showing `RiStopFill` — never both handlers wired to the same click.

## Everything else (unchanged)

`useTextToSpeech.ts`, `useVoiceAnalyzer.ts`, `voiceProviderStatus.ts`, `synthesizeSpeech`
(`voiceApi.ts`), and `AiPresenceCard.tsx`'s own consumption of `tts.getIntensity`/
`tts.isSpeaking` for the reactive sphere are unmodified — this feature only adds a second
*trigger* (manual replay) into the same existing playback pipeline, and one new piece of
page-level state to track which message that pipeline is currently voicing.
