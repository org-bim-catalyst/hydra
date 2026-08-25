# Contract: Composer & Voice-Control Layout

## `ChatComposer` (props changes)

New optional props (additive, existing props unchanged):

```ts
continuousAnalyzer?: {
  state: VoiceAnalyzerState
  getIntensity: () => number
}
```

- Passed by `ChatPage` only while `composerVisualState === 'continuous'` is reachable
  (`conversationMode === 'Continuous'`); reuses the same `analyzerState`/`analyzerIntensity`
  values already computed for the Ai presence sphere (Decision 4).
- When absent, the continuous-state waveform renders in an idle/quiet visual (no functional
  regression for any caller that doesn't pass it — none currently exist outside `ChatPage`).

Behavioral contract per `composerVisualState` (supersedes the current row layout only —
`composerVisualState`'s own derivation is unchanged):

- `empty`: attachment control renders first (leading edge); mic + continuous-entry render last
  (trailing edge), with the spacer between the two groups, not after both.
- `typing`: attachment + mic render at the leading edge (mic is the *same* DOM element used in
  `empty`/`recording` — see Decision 2); Send renders at the trailing edge.
- `recording` (awaiting tap review): cancel renders at the leading edge, waveform in the middle,
  finish at the trailing edge.
- `recording` (actively holding): unchanged from today (waveform, then the mic-fill indicator).
- `continuous`: waveform renders at the leading edge (flex-grow, filling available space); mute +
  exit render at the trailing edge.

## `RecordingReviewControls` (props changes)

```ts
export interface RecordingReviewControlsProps {
  phase: RecordingPhase
  onFinish: () => void
  onCancelRecording: () => void
  /** Rendered between the reordered cancel/finish controls — e.g. ChatComposer's live
   * waveform. Omitted entirely by CollapsedVoiceControls, which renders its own waveform
   * separately above this component. */
  middle?: React.ReactNode           // NEW
  placement?: 'left' | 'right' | 'bottom'   // widened from 'left' | 'right'; default becomes 'bottom'
}
```

- Render order becomes: `cancel`, then `middle` (if provided), then `finish` — previously
  `finish`, then `cancel`, with no `middle` slot.
- `onFinish`/`onCancelRecording` semantics are unchanged; only visual order and the new slot are
  added.

## `CollapsedVoiceControls` (no prop changes)

- Every `Tooltip`'s `placement` prop changes from `"left"` to `"bottom"` (Decision 7). No change
  to the exported `VoiceControlsProps` contract or any callback signature.

## `ChatPage` (internal change only, no exported contract change)

- Adds one `useEffect` (Decision 5) that calls `conversationAudio.startTurn()` once
  `conversationMode === 'Continuous' && chatId && providerId && modelId && conversationAudio.voiceState === 'Idle'`.
  Does not change `voiceControlsProps`, `handleStartCapture`, or `handleToggleMode`'s existing
  signatures/call sites.
