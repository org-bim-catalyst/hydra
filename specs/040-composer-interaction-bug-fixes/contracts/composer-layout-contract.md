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

- `empty` (PushToTalk): attachment control renders first (leading edge); spacer fills the gap;
  mic + continuous-entry render last (trailing edge).
- `typing` (PushToTalk — Figure 2): attachment renders at the leading edge; spacer fills the gap;
  mic + Send render together at the trailing edge. Mic is the *same* DOM element used in
  `empty`/`recording` (see Decision 2).
- `typing` (Continuous — Figure 5): attachment renders at the leading edge; spacer fills the gap;
  Send renders at the trailing edge. The mic control is **not shown** — it is already active in
  the background and showing it would be redundant.
- `recording` (awaiting tap review — Figure 3): cancel renders at the leading edge; waveform
  (`flex: 1`, filling available space) renders in the middle; finish at the trailing edge.
- `recording` (actively holding — Figure 9): waveform (`flex: 1`, filling available space)
  renders at the leading edge; mic-fill indicator at the trailing edge.
- `continuous` (Figure 4): waveform (`flex: 1`, filling available space) renders at the leading
  edge; mute + exit render at the trailing edge.

**Waveform sizing rule**: every waveform that appears in the expanded composer uses
`sx={{ flex: 1 }}` so it fills the available row space. The collapsed widget
(`CollapsedVoiceControls`) is the only context where a fixed narrow width is appropriate.

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
