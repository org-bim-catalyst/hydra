# Contract: Expanded Chat Panel — Consolidated Voice Control

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md) | Implements FR-004–FR-008, FR-006a.

**Supersedes**, for the Expanded layout only:
`specs/026-floating-chat-assistant/contracts/chat-widget-components.md`'s "CollapsedVoiceControls
/ ExpandedChatPanel's voice-controls footer" section, which described `VoiceControlBar`
and `CollapsedVoiceControls` as two presentational layouts sharing one `VoiceControlsProps`
contract. That sharing is now broken deliberately: `CollapsedVoiceControls` (Collapsed
widget) is unchanged and out of scope here — it was never duplicated and remains the
existing single-surface reference. `VoiceControlBar` is retired from the Expanded tree;
its responsibilities move into `ChatComposer`, described below. Types are illustrative,
not final implementation code (bodies are a `/speckit-tasks` + implementation concern).

## ChatComposer (Expanded layout — extended)

```ts
interface ChatComposerProps {
  value: string
  onChange: (text: string) => void
  onSend: () => void
  disabled?: boolean
  onInsertPromptClick?: () => void
  onTranslateLastClick: () => void          // NEW — relocated from the top Toolbar (FR-007)

  // Voice — superset of today's props; conversationMode/isListening/permissionState/
  // captureError/onStartCapture/onStopCapture/onClearCaptureError are unchanged in meaning
  // from the current ChatComposerProps. onCancelCapture is dropped (found during
  // implementation to have no remaining distinct use once consolidated — Push-to-Talk
  // cancellation is owned by RecordingReviewControls' own cancel button, rendered
  // throughout recording.phase !== 'idle'; Continuous mode has no separate cancel concept
  // beyond the mic's own start/stop toggle. The underlying recorder.cancel()/
  // recognition.cancel() capability is unaffected, only this redundant prop is gone).
  conversationMode: 'PushToTalk' | 'Continuous'
  isListening: boolean
  permissionState: MicrophonePermissionState
  captureError: string | null
  onStartCapture: () => void
  onStopCapture: () => void
  onClearCaptureError: () => void

  onToggleMode: () => void                  // NEW — absorbed from VoiceControlBar
  recording?: {                              // NEW — absorbed from VoiceControlBar/
    phase: 'idle' | 'recording' | 'reviewing' | 'transcribing'   // CollapsedVoiceControls'
    getIntensity: () => number                                    // shared `recording`
    onFinish: () => void                                          // shape, reused as-is
    onCancelRecording: () => void
    onAccept: () => void
  }

  isMuted: boolean                          // NEW — speaker-output mute (FR-006a); unrelated to the mic
  onToggleMute: () => void                  // NEW — merged mute+stop handler (FR-006a/b, Decision 5a): the
                                             // caller (ChatPage.tsx) MUST call tts.stop() before/alongside
                                             // updateVoicePreference({ isMuted: true }) when tts.isSpeaking
                                             // is true. ChatComposer itself needs no isSpeaking prop — the
                                             // decision of whether to also stop playback is made by the
                                             // caller, not this component (Decision 5a).
}
```

**Contract guarantees**:

- Exactly one mic icon renders at all times, in both `conversationMode` values —
  `showMicButton`'s current `conversationMode === 'PushToTalk'` gate (`ChatComposer.tsx:150`)
  is removed; the icon always renders, and its behavior branches on
  `conversationMode` (FR-004).
- In `Continuous` mode, the mic icon is the microphone mute toggle
  (`isListening ? onStopCapture() : onStartCapture()`) — tapping it stops the app
  listening to the user (and, by extension, Lucy responding to what she'd have heard),
  with no separate always-visible icon for this (FR-006, Clarification Q3). This is
  distinct from `isMuted`/`onToggleMute` below, which mutes Lucy's *spoken output*, not
  the microphone.
- In `Push-to-Talk` mode, the existing tap-vs-hold gesture handling
  (`ChatComposer.tsx:62-148`, unchanged) governs start/stop; while
  `recording.phase !== 'idle'`, the mic icon area is replaced by `RecordingReviewControls`
  exactly as `VoiceControlBar`/`CollapsedVoiceControls` render it today — same component,
  reused, not reimplemented (FR-005).
- No "Listening…" text label accompanies the mic icon in either mode (FR-014) — its
  existing pulse animation while `isListening` (`ChatComposer.tsx:203-213`, unchanged) is
  the sole indicator that capture is active; `VoiceControlBar.tsx:169`'s equivalent text
  is not carried over.
- A menu/popover anchored to the mic icon exposes only the Continuous/Push-to-Talk mode
  switch (`onToggleMode`) — disabled while a Push-to-Talk capture is in progress, same
  guard as today's `isModeSwitchBlocked` in `VoiceControlBar.tsx:56`. No microphone
  hardware device selection is included (research.md Decision 5, Scope note).
- Exactly one recording-status indicator and one error/permission-denied surface exists
  (`captureError`/`permissionState`, already present in `ChatComposer`) — `onClearError`/
  `errorMessage` from the old `VoiceControlsProps` are not threaded here separately,
  because `ChatPage.tsx` already sources both from the same underlying
  `recorder.error`/`recognition.error` value (confirmed in research.md Decision 5).
- `isMuted`/`onToggleMute` (FR-006a/b) renders as a single, persistent, always-visible
  icon alongside the mic — never inside the mic's mode-switch menu — and is the *only*
  control for both muting future replies and interrupting a reply in progress. There is
  no separate "stop" button. Pressing it while `isSpeaking` is true silences the current
  reply immediately, in addition to setting `isMuted`; pressing it while not speaking
  just sets `isMuted`. Unmuting never resumes a previously interrupted reply.
- The "Lucy is speaking…" text label (`VoiceControlBar.tsx:172-174`) is dropped with no
  replacement in this row (FR-013) — `AiPresenceCard` (`ChatPage.tsx:176`), a persistent
  reactive presence indicator rendered independently of the chat panel's expand/collapse
  state and already driven by `tts.getIntensity`, already conveys this (research.md
  Decision 5a). `VoiceAnalyzer`/`analyzerState` (`ChatPage.tsx:555-556`,
  `CollapsedChatControl`-only) is a separate concern — it reflects the *user's*
  microphone activity, not Lucy's, and is unaffected by this feature either way.
- `onTranslateLastClick` renders as a small icon in the same row, visually distinct from
  the voice controls (spec.md User Story 4, Acceptance Scenario 4) — same handler
  (`handleTranslateLast`, `ChatPage.tsx:445-450`, unchanged), only its rendering location
  changes.

## ChatPage.tsx (`ConversationView` — Expanded branch)

**Contract guarantees**:

- `<VoiceControlBar {...voiceControlsProps} />` (`ChatPage.tsx:659`) is removed from this
  render tree. `voiceControlsProps` (`ChatPage.tsx:502-542`) is still computed — its
  fields now flow into the extended `ChatComposerProps` above instead.
- The `Toolbar` at `ChatPage.tsx:578-591` keeps `ProjectPicker` only; the
  `RiTranslate2` `IconButton` is removed from it. The `Toolbar`'s `sx` is given an
  explicit, smaller height than the MUI `dense` variant default, so removing one icon
  measurably shrinks the row (research.md Decision 6) rather than leaving a fixed-height
  row with one less icon in it.
- `ExpandedChatPanel`'s header (`ExpandedChatPanel.tsx`) and its `headerTrailing` slot are
  unmodified — `ProjectPicker` does not move there (research.md Decision 6).

## Everything else (unchanged)

`CollapsedVoiceControls.tsx`, `RecordingReviewControls.tsx`, `VoiceAnalyzer.tsx`,
`useVoiceRecorder.ts`, `useSpeechRecognition.ts`, and every other `/api/v1/ai/*` or
`/hubs/*` contract are unmodified by this feature.
