# Contract: Voice Control Integration (Frontend)

No new or changed REST endpoints. This feature reuses
`GET/PUT /api/v1/ai/voice/preferences` exactly as documented in
[../../012-elevenlabs-voice-engine/contracts/voice-preferences.md](../../012-elevenlabs-voice-engine/contracts/voice-preferences.md),
unchanged. `useConversationAudio`'s streaming voice-reply contract
(`voice-reply-stream.md`) is **not** used by this feature (research.md Decision 1) — output
continues to use the existing per-message `synthesizeSpeech`/`/api/v1/ai/voice/speak`
contract `useVoiceOutput` already calls. This document captures the **frontend integration
contract** this feature changes.

## `useVoiceOutput` (existing hook — extended)

```ts
// New return fields, added to the existing speak/stop/isSpeaking/getIntensity/error/clearError:
{
  isMuted: boolean
  setMuted: (muted: boolean) => void   // stops current playback immediately if turning on;
                                        // future speak() calls no-op while true (Decision 3)
  toggleMute: () => void
}
```

`ChatPage.tsx` keeps an effect syncing `isMuted` from `voicePreferencesStore.isMuted` into
this hook (one-directional: store → hook), and calls `voicePreferencesStore.update({
isMuted })` from the mute control's click handler (store is still the persistence source
of truth, matching every other preference).

`speak(text, language)`: unchanged signature; internally becomes a no-op when `isMuted` is
true (no network call, no playback).

`stop()`: unchanged, already exists — now also invoked internally the instant `setMuted(true)`
is called while `isSpeaking` is true.

## `useSpeechRecognition` (existing hook — no signature change, new call site and owner)

**Ownership**: instantiated in `ConversationView` (`ChatPage.tsx`), the same level that
already owns `tts = useVoiceOutput()` and already passes it down as a prop — this mirrors
that existing lifted-hook convention rather than introducing a new one. `mode`
(`'push-to-talk' | 'continuous'`) is sourced from `voicePreferencesStore.conversationMode`.

`ConversationView` passes down to `ChatComposer` (as new props, replacing its current
internal `voice = useWavRecorder()`):
- `isListening`, `permissionState`, `error`, `deviceNotice` (recognition state to render)
- `onStartCapture`/`onStopCapture`/`onCancelCapture` (wrapping `recognition.start`/`stop`/`cancel`)
- an `onFinalTranscript`-driven callback that branches on `conversationMode`:
  - `PushToTalk` → fills the composer's text field (`setText`), same as today's
    `handleConfirmVoice` behavior — no auto-send.
  - `Continuous` → calls `onSend`/`send()` directly with the transcript — no manual step.

`VoiceControlBar` (also rendered by `ConversationView`, alongside/above `ChatComposer`)
reads `isListening`/`permissionState`/`error` from the same lifted `useSpeechRecognition`
instance for its mic-state display, and `isSpeaking`/`isMuted`/`error` from `tts` for its
mute/speaking display — one recognition instance and one output instance, each shared by
both consumers that need them, not duplicated per component.

## `VoiceControlBar` (existing component — adapted prop contract)

```ts
export interface VoiceControlBarProps {
  isAvailable: boolean            // recognition.isSupported && tts.isSupported
  isListening: boolean            // recognition.isListening — replaces `voiceState`
  isSpeaking: boolean             // tts.isSpeaking
  isMuted: boolean                // tts.isMuted
  conversationMode: 'PushToTalk' | 'Continuous'
  errorMessage: string | null     // recognition.error ?? tts.error
  permissionState: MicrophonePermissionState  // recognition.permissionState (FR-009)
  onStart: () => void             // recognition.start (hold-press or first toggle-click)
  onStop: () => void              // recognition.stop (hold-release or second toggle-click)
  onCancel: () => void            // recognition.cancel (discard in-progress capture)
  onStopSpeaking: () => void      // tts.stop (the existing "Stop the reply" affordance)
  onToggleMode: () => void        // disabled while isListening && conversationMode === 'PushToTalk'
  onToggleMute: () => void
  onClearError: () => void
}
```

This replaces the previous `voiceState: VoiceStateName` prop (which reflected
`useConversationAudio`'s 9-state turn machine, not used by this feature — research.md
Decision 2) with the flatter `isListening`/`isSpeaking`/`isMuted`/`permissionState` fields
`useSpeechRecognition`/`useVoiceOutput` already expose. Markup, icons, tooltips, and the
existing keyboard-operability pattern (`VoiceControlBar.test.tsx`'s jest-axe check) are
preserved — only the props feeding them change.

**New interaction handlers required on the mic control** (Decision 5, research.md):
`onPointerDown`/`onPointerUp`, `onTouchStart`/`onTouchEnd` → `onStart`/`onStop` (hold), in
addition to the existing `onClick` toggle path.

## `voicePreferencesStore` (existing store — no change)

`update({ isMuted })` and `update({ conversationMode })` already exist and already persist
to the backend with local-state rollback on failure (constitution §2.VIII compliance
already in place). Both `VoiceControlBar`'s handlers and the `ChatComposer` mic control's
mode-read call this/read this directly.

## Removed integration points

- `ChatComposer.tsx` no longer imports `useWavRecorder` or `transcribeMicrophoneAudio`;
  its file-attach and text-send behavior are unchanged.
- Nothing is removed from `ChatPage.tsx`'s auto-speak effect — it is kept and gated by
  `tts.isMuted` (Decision 3, research.md), not deleted.
- `useConversationAudio.ts` and `useVoiceState.ts` are not touched or imported by this
  feature.
