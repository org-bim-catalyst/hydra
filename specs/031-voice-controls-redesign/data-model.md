# Data Model: Voice Controls & Composer Redesign

This feature introduces no backend/database entities and no new client-side stores (spec.md Key
Entities: N/A). It changes the shape of one existing hook's return type and two components'
props. Documented here in place of a traditional data model.

## `RecordingPhase` (client-only, `useVoiceRecorder.ts`)

**Before**: `'idle' | 'recording' | 'reviewing' | 'transcribing'`

**After**: `'idle' | 'recording' | 'transcribing'`

`'reviewing'` is removed (research.md Decision 1) — no code anywhere is expected to observe this
value once removed. `finish()` changes from `() => void` (fire-and-forget, moves to `'reviewing'`)
to `() => Promise<string>` (stops, transcribes, resolves with the transcript, moves straight to
`'idle'` — mirroring `accept()`'s current return contract, which `finish()` now absorbs).
`accept()` is removed from the hook's returned object entirely (dead once `'reviewing'` doesn't
exist).

## `RecordingReviewControlsProps` (`RecordingReviewControls.tsx`)

| Field | Before | After |
|-------|--------|-------|
| `phase` | `RecordingPhase` (4 values) | `RecordingPhase` (3 values) |
| `onFinish` | `() => void` | `() => void` (unchanged signature; caller now awaits transcription internally) |
| `onCancelRecording` | `() => void` | `() => void` (unchanged) |
| `onAccept` | `() => void` | **removed** |
| `placement` | `'left' \| 'right'` | unchanged |

## `VoiceControlsProps.recording` (shared contract, `CollapsedVoiceControls.tsx` + `ChatComposer.tsx`)

| Field | Before | After |
|-------|--------|-------|
| `phase` | `RecordingPhase` (4 values) | `RecordingPhase` (3 values) |
| `getIntensity` | `() => number` | unchanged |
| `onFinish` | `() => void` | unchanged signature |
| `onCancelRecording` | `() => void` | unchanged |
| `onAccept` | `() => void` | **removed** |

## `ChatComposerProps` (`ChatComposer.tsx`)

| Field | Change |
|-------|--------|
| `isMuted` | **removed** (mute control relocates to `ExpandedChatPanelProps`, research.md Decision 5) |
| `onToggleMute` | **removed** |
| `onTranslateLastClick` | **removed** (research.md Decision 4) |
| All other fields | unchanged |

## `ExpandedChatPanelProps` (`ExpandedChatPanel.tsx`)

Gains two new required props, mirroring specs/030-composer-panel-refinements' `isFullHeight`/
`onToggleHeight` pattern:

| Prop | Type | Purpose |
|------|------|---------|
| `isMuted` | `boolean` | Drives the relocated mute/unmute-Lucy control's icon/label. |
| `onToggleMute` | `() => void` | Wired to that control's `onClick`. |

## `ChatPage.tsx` internal wiring changes

- `handleRecorderAccept` is replaced by `handleFinishAndTranscribe` (awaits the new async
  `recorder.finish()` instead of `recorder.accept()`; same append-to-`composerText` logic).
- `voiceControlsProps.recording.onFinish` now points at `() => void handleFinishAndTranscribe()`;
  `recording.onAccept` is removed from the object literal (no longer part of the shared contract).
- `handleTranslateLast` and the `onTranslateLastClick` prop passed to `ChatComposer` are removed.
- `isMuted`/`onToggleMute` move from the `<ChatComposer>` JSX call to the `<ExpandedChatPanel>` call.
