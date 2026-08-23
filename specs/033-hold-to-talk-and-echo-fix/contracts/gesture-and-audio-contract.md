# Contract: Pure Hold-to-Talk Gesture, Transcription Error Response, and Mic Muting

## 1. `POST /api/v1/ai/transcriptions` — new error response for a malformed 2xx

**Existing, unchanged responses** (from SPEC-032 — regression clarity only):

| Condition | Status | `type` |
|---|---|---|
| OpenAI returns 401/403 | 502 | `.../ai-provider-authentication-failed` |
| OpenAI returns 429 | 429 | `.../ai-provider-rate-limited` |
| OpenAI returns another 4xx | 400 | `.../ai-provider-request-invalid` |
| OpenAI returns 5xx / connection fails (after retry) | 502 | `.../ai-provider-unavailable` |

**New response** (this feature):

| Condition | Status | `type` | `detail` |
|---|---|---|---|
| OpenAI returns 2xx but the body can't be parsed, or is missing `text` | **502** | `https://hydra.bimcatalyst.com/problems/ai-provider-unavailable` (reused, not new) | `"The AI service could not process your request. Please try again."` (the existing `AiProviderUnavailableException` message, unchanged) |

## 2. `ChatComposer.tsx` — pure hold-to-talk gesture contract

**Before**: `pointerdown`/`keydown(Space)` starts capture unconditionally; `pointerup`/
`pointerleave`/`keyup(Space)` stops capture **only if held ≥ 350ms** (`HOLD_THRESHOLD_MS`),
otherwise leaves it running, requiring a later, separate tap (`onClick`) or a `RecordingReviewControls`
Finish (✓) button press to complete it. A visible Cancel (✗) button is also shown during the
recording via `RecordingReviewControls`.

**After**:
- `pointerdown` on the mic button (idle, Push-to-Talk mode): calls
  `event.currentTarget.setPointerCapture(event.pointerId)`, then `onStartCapture()`. No
  `isListening` guard needed (no competing toggle-off click path remains).
- `pointerup` / `pointerleave` / `pointercancel` / `keyup(Space)`: **always** calls
  `onStopCapture()` (already `handleFinishAndTranscribe` per `ChatPage.tsx`'s wiring) —
  duration is no longer checked at all.
- The mic `IconButton` stays mounted as the same element for the entire gesture (press → recording
  → releasing → briefly `'transcribing'` → back to idle) — only its visual state (color/icon/
  disabled) changes; it is never unmounted/replaced by a different component mid-gesture.
- `RecordingReviewControls` is **no longer rendered inside `ChatComposer.tsx`**. No Finish button
  (redundant — release *is* finish) and no Cancel button (unreachable — releasing always finishes;
  per the resolved clarification, discarding an unwanted recording happens after the fact by
  editing/not-sending the resulting draft text, not before).
- `handleMicClick` (the residual plain-click path), `captureStartedAtRef`, `suppressNextClickRef`,
  and `HOLD_THRESHOLD_MS` are removed entirely — no longer needed once there is exactly one
  gesture.

**Explicitly unaffected** (scope boundary — see research.md Decision 3's correction):
`CollapsedVoiceControls.tsx`'s Push-to-Talk mic remains a plain click-to-start/click-to-stop
control using `RecordingReviewControls`' Finish/Cancel buttons exactly as today — it has no
hold/release ambiguity and was never the subject of the reported bug. `RecordingReviewControls.tsx`
itself, `useVoiceRecorder.ts`'s `cancel()`, and `ChatPage.tsx`'s `voiceControlsProps` wiring are
unchanged.

## 3. `useSpeechRecognition.ts` — new `setInputMuted` contract

**New function**: `setInputMuted(muted: boolean): void` — toggles
`streamRef.current?.getAudioTracks().forEach(t => { t.enabled = !muted })`. No-ops safely if no
stream is currently active (e.g., called before `start()` or after `stop()`/`cancel()`). Does not
tear down or reconnect the `AudioContext`, `AudioWorkletNode`, or the ElevenLabs WebSocket.

**Caller contract** (`useConversationAudio.ts`): calls `recognition.setInputMuted(true)` on
entering `'AiSpeaking'`, and `setInputMuted(false)` when leaving it (turn completion, `stop()`, or
any other transition away from `'AiSpeaking'`). No other caller needs to invoke this — it is fully
internal to the Continuous-mode conversation loop.

**Removed** (dead once the mic is muted during the only phase they mattered):
`handleLocalSpeechLikely`, `isDuckedRef`, `duckTimeoutRef`, `clearDuckTimeout`, the `'Interrupted'`
`voiceState.setState(...)` call and its associated 1500ms timeout, and the `onLocalSpeechLikely`
prop/parameter threading between `useConversationAudio.ts` and `useSpeechRecognition.ts`.
`VoiceStateName`'s `'Interrupted'` value itself stays in the type (unused now, not removed — a
separate, unrelated cleanup).

**New `getUserMedia` constraint**: `echoCancellation: true` added to the audio constraint object
(alongside the existing `deviceId` handling), as defense-in-depth alongside the primary mute fix.
