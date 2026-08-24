# Contract: Expanded Chat Panel — State-Dependent Composer Actions

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)
| Implements FR-001–FR-019.

**Supersedes**, for the Expanded layout's `ChatComposer` only:
`specs/029-fix-chat-widget-bugs/contracts/expanded-voice-control-consolidation.md`'s
guarantee that "Exactly one mic icon renders at all times, in both `conversationMode`
values" — that guarantee is now qualified: the mic (and the continuous-conversation action)
render only in the *empty* and *typing* composer states, not during an active recording or
during Continuous mode's idle-listening/typing states, per the state table below. Everything
else that contract established (gesture handling, `RecordingReviewControls` reuse, mute
being a separate concern from mic capture) is unchanged and still governs. Types below are
illustrative, not final implementation code.

## Composer visual states → visible actions

| State | Trigger to enter | Attach | Mic (mic-line/mic-fill) | Continuous-conversation (voiceprint-line) | Cancel (close-line) | Confirm (check-line) | Send | Mute (mic-off-line) | Exit (stop-line) |
|---|---|---|---|---|---|---|---|---|---|
| Empty | Initial load / text cleared / message sent | ✅ | ✅ | ✅ | – | – | – | – | – |
| Typing | First character typed | – | – | – | – | – | ✅ (enabled) | – | – |
| Click-to-talk recording | Click (not hold) on mic | – | – | – | ✅ | ✅ | – | – | – |
| Hold-to-talk recording | Press-and-hold mic | – | ✅ (`mic-fill`, non-interactive indicator) | – | – | – | – | – | – |
| Continuous — idle-listening | Continuous-conversation action activated (or return from typing-within-continuous) | – | – | – | – | – | – | ✅ | ✅ |
| Continuous — typing | Type while continuous idle-listening | – | – | – | – | – | ✅ (enabled) | – | – |

Notes:
- "Typing" and "Continuous — typing" both show only the send action in the composer's
  action row — they differ in that Continuous mode keeps listening in the background and,
  on send or on clearing the field, returns to Continuous idle-listening (not to Empty).
- The attach action's own behavior (`fileInputRef.current?.click()`) is unchanged; only its
  *visibility* becomes state-dependent instead of always-mounted.
- `RiArticleLine` (saved-prompts / `onInsertPromptClick`) is removed from every state — no
  row above lists it because it never appears (FR-018).

## `ChatComposer` (Expanded layout — extended)

```ts
interface ChatComposerProps {
  value: string
  onChange: (text: string) => void
  onSend: () => void
  disabled?: boolean

  // onInsertPromptClick REMOVED (FR-018) — the prop and its rendering branch are deleted,
  // not merely made to always return null; RiArticleLine import is dropped from this file.

  conversationMode: 'PushToTalk' | 'Continuous'
  isListening: boolean
  permissionState: MicrophonePermissionState
  captureError: string | null
  onStartCapture: () => void
  onStopCapture: () => void
  onClearCaptureError: () => void

  // CHANGED semantics (research.md Decision 3): the caller (ChatPage.tsx) implements the
  // one-click hybrid — onToggleMode's handler, when switching PushToTalk → Continuous, MUST
  // await voicePreferencesStore.update({ conversationMode: 'Continuous' }) before invoking
  // onStartCapture() (never start capture against a preference save that may still roll
  // back); when switching Continuous → PushToTalk it MUST invoke onStopCapture()
  // immediately/synchronously — listening stops with no delay regardless of the save's
  // outcome — then await the inverse update() for its own pre-existing error surfacing
  // (analysis remediation C1). ChatComposer itself only calls onToggleMode — it does not
  // need to know the pairing or ordering happens.
  //
  // Round-3 finding F5: the "onStartCapture()" this handler calls MUST be the same
  // handleStartCapture reference contracts/reply-playback-control.md's E4 fix wraps (also
  // passed down as this same prop) — not an independent raw capture-start call — so that
  // entering Continuous mode also stops an in-progress manual replay, consistent with
  // click-to-talk/hold-to-talk (which get this for free by calling the prop value directly).
  onToggleMode: () => void

  recording?: {
    phase: RecordingPhase
    getIntensity: () => number
    onFinish: () => void
    onCancelRecording: () => void
  }

  voicePreferencesUnavailable?: boolean
}
```

**Contract guarantees**:

- The mic, continuous-conversation action, and attach action are rendered **only** when
  `value === '' && recording?.phase is 'idle'/undefined && !(conversationMode === 'Continuous'
  && isListening)` — i.e., the Empty state row above. This replaces today's
  `!isRecordingActive` as the sole gate with a gate that also excludes non-empty `value` and
  active Continuous listening (FR-001, FR-002, FR-012).
- The send action (`RiSendPlane2Fill`) is rendered **only** when `value !== ''` (typing, in
  either Push-to-Talk or Continuous mode) — never simultaneously with mic/continuous-
  conversation/mute/exit (FR-002, FR-015). Its `disabled` condition (`!value.trim()`) is
  unchanged from today.
- Click-to-talk (`RecordingReviewControls`) rendering condition is unchanged from today's
  `isAwaitingTapReview && recording` — this is the "tap" branch of the existing gesture
  logic (research.md Decision 1), already producing Figure 3 exactly.
- Hold-to-talk recording (`recording?.phase === 'recording' && !isAwaitingTapReview`, i.e.
  the "hold" branch) swaps the mic icon to `RiMicFill` for the duration and shows the
  existing `VoiceAnalyzer` waveform sibling — no `RecordingReviewControls` render in this
  branch (FR-009), matching Figure 9.
- **Indefinite-recording safeguard** (analysis remediation E1, corrected per round-2 finding
  F4; spec.md Edge Case): while a gesture is in progress (`isCapturingRef.current === true`,
  regardless of whether elapsed time has crossed `HOLD_THRESHOLD_MS` yet), a `document`
  `visibilitychange` (hidden) or `window` `blur` listener MUST call `onStopCapture()`
  **directly** — not by routing through `resolveGestureOnRelease()`, whose elapsed-time
  dispatch would leave a still-tap-classified press in the `isAwaitingTapReview` state
  (capture still running, waiting for a Finish/Cancel the user can't reach with the tab
  hidden) instead of actually stopping it. This covers a tab losing focus or the device
  screen locking mid-gesture, neither of which reliably fires
  `pointerup`/`pointerleave`/`pointercancel` on every platform. The listener is attached only
  while capturing and removed on resolution, mirroring the existing `setPointerCapture`
  cleanup discipline.
- In Continuous idle-listening (`conversationMode === 'Continuous' && isListening &&
  value === ''`), the action row shows exactly two icons: `RiMicOffLine`/`RiMicLine` bound to
  a **mute** handler (toggles audio *input* capture without leaving Continuous mode — distinct
  from `onStopCapture`, which today's Continuous mic reuses for both; this feature separates
  them per FR-013/FR-014) and `RiStopLine` bound to **exit** (`onToggleMode` paired with
  `onStopCapture`, per the one-click-hybrid symmetry above).
- The agent's circular avatar (`LucyPortrait`, currently unused in `ChatPage.tsx`'s message
  area) is conditionally rendered in the conversation view — not inside `ChatComposer` itself
  — only when `conversationMode === 'Continuous' && isListening` (FR-012, both idle-listening
  and typing-within-continuous sub-states, since Figure 5/6 both show it).

## `ExpandedChatPanel` (header — height controls)

```ts
// ExpandedChatPanelProps unchanged in shape; only the rendered icons change.
```

**Contract guarantee**: The height-toggle `IconButton`
(`ExpandedChatPanel.tsx:153-162`) swaps `RiExpandVerticalLine` → `RiExpandDiagonalLine` and
`RiCollapseVerticalLine` → `RiCollapseDiagonalLine`. `onToggleHeight`/`isFullHeight` and all
surrounding behavior (FR-019) are unchanged.

## `CollapsedVoiceControls` (icon parity only)

**Contract guarantee**: `RiFingerprintLine` → `RiVoiceprintLine` in the mode-switch
`IconButton` (`CollapsedVoiceControls.tsx:120-125`) — icon swap only, per research.md
Decision 8. `RiInfinityLine`, the vertical `Stack` layout, and all handlers are unchanged.
This surface does **not** gain hold-to-talk-specific visuals, a one-click-hybrid change (it
already only has a single mic toggle in Continuous mode, not two paired actions to reconcile
— out of scope here), or a replay control (no message list renders in this surface).

## Everything else (unchanged)

`RecordingReviewControls.tsx`, `VoiceAnalyzer.tsx`, `useVoiceRecorder.ts`,
`useSpeechRecognition.ts`, `voiceApi.ts`, `voicePreferencesStore.ts`'s internal `update`
logic, and every `/api/v1/ai/*` or `/hubs/*` backend contract are unmodified by this feature.
