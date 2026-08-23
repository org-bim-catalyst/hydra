# UI Contract: Push-to-Talk Flow, Composer Declutter, Panel Header

Scope: `useVoiceRecorder.ts`, `RecordingReviewControls.tsx`, `ChatComposer.tsx`,
`ExpandedChatPanel.tsx`. Internal component/DOM contract, no public HTTP API.

## Push-to-Talk recording state machine (`useVoiceRecorder.ts`)

```
idle ──start()──> recording ──finish()──> transcribing ──(resolves)──> idle
  ^                    │
  └──────cancel()──────┘
```

- `start()`: unchanged — begins capture, phase → `recording`.
- `finish()`: **changed**. Stops the `MediaRecorder`, awaits the resulting blob, transitions
  directly to `transcribing`, calls `transcribeAudio`, resolves with the transcript string (or
  `''` + sets `error` on failure), transitions to `idle`. No more `reviewing` stop-over.
- `cancel()`: unchanged. Valid from `recording` only (no-ops from `idle`/`transcribing`).
  Discards captured audio, phase → `idle`.
- `accept()`: **removed**.

## `ChatComposer` footer visibility (Push-to-Talk mode)

| State | Visible footer content |
|-------|------------------------|
| Idle (`recorder.phase === 'idle'`) | attach, insert-prompt, mic (tap-or-hold), mode-switch icon+menu, voice-preferences-warning (if any), Send |
| Recording (`recorder.phase === 'recording'`, tap- or hold-started) | live waveform, Finish (✓), Cancel (✗), Send — attach/insert-prompt/mode-switch/voice-preferences-warning are hidden |
| Transcribing (`recorder.phase === 'transcribing'`) | same visibility as Recording (brief, auto-resolving) |

Continuous mode is unaffected — it never populates `recording`, so its footer is unchanged by this
contract (mic mute/unmute toggle + Send, as today).

## `ExpandedChatPanel` header order (unchanged positions except the new mute control)

```
<Stack row>
  IconButton  Collapse
  LucyPortrait + name/status
  IconButton  Mute/Unmute Lucy      NEW — immediately after name/status, before the language flag
  ActiveLanguageFlag
  IconButton  Start new conversation
  IconButton  <resize/toggle>        (specs/030-composer-panel-refinements)
  {headerTrailing}
</Stack>
```

- Icon/label: `RiVolumeUpLine`/`'Mute Lucy'` when `!isMuted`, `RiVolumeMuteLine`/`'Unmute Lucy'`
  when `isMuted` — same icon pair `ChatComposer.tsx` used before relocation.
- Tooltip title matches the `aria-label`, per the tooltip-reuse convention established in
  specs/030-composer-panel-refinements.
- Behavior (including stopping in-progress speech when muted) is unchanged — only the prop source
  moves from `ChatComposerProps` to `ExpandedChatPanelProps`.

## Removed surface

- `ChatComposer`'s translate `Tooltip`+`IconButton` (`RiTranslate2`, "Translate last response") —
  gone entirely, no replacement control.
- `ChatComposerProps.onTranslateLastClick`, `.isMuted`, `.onToggleMute` — removed from the type.
- `ChatPage.tsx`'s `handleTranslateLast`, `useChatStream.ts`'s `sendTranslation` — removed.
- `RecordingReviewControls`'s `reviewing`-phase Accept button and `onAccept` prop — removed.
