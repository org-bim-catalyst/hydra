# Contract: Upload Guard, Dual Gesture, and Dedicated Voice View

## 1. `POST /api/v1/ai/transcriptions` and `/transcriptions/microphone` — new guard response

| Condition | Status | Body |
|---|---|---|
| `file` is null or has zero length | **400** | `ProblemDetails { Title: "No audio file was provided", Status: 400 }` |
| Well-formed file, any downstream provider failure | Unchanged from specs/032/033 | — |

This is a direct controller-level guard, not a thrown/caught exception — it never reaches
`ProblemDetailsMiddleware`'s classification switch.

## 2. `ChatComposer.tsx` — dual tap/hold gesture contract

**On `pointerdown`/`keydown(Space)`** (unchanged from specs/033): `setPointerCapture`, start
recording. Same mic `IconButton` element stays mounted; only the waveform renders alongside it.
No confirm/discard controls at this point, regardless of what the press will resolve to.

**On `pointerup`/`pointerleave`/`pointercancel`/`keyup(Space)`**: measure elapsed time since press.

| Elapsed | Resolution | Visible result |
|---|---|---|
| `< HOLD_THRESHOLD_MS` | **Tap** | Recording continues. Mic button replaced by `RecordingReviewControls` (✓ Finish / ✗ Cancel) + waveform. Waits for explicit action. |
| `>= HOLD_THRESHOLD_MS` | **Hold** | Recording stops immediately, transcribes, populates the message field. No controls ever shown. |

**Tap-resolved state, explicit actions**:
- Finish (✓): same finish function a hold-release calls — stop, transcribe, populate message field.
- Cancel (✗): `recording.onCancelRecording` (`recorder.cancel()`) — discard, nothing transcribed.

**Unaffected**: `CollapsedVoiceControls.tsx`'s own click-to-record-with-review-controls flow —
already implements the same pattern this decision reuses; not touched by this feature.

## 3. Dedicated Continuous voice view — entry/exit contract

**Entry**: `ChatPage.tsx`'s `handleToggleMode`, when switching Push-to-Talk → Continuous, sets a
transient `isVoiceViewActive` flag (in addition to updating the persisted `conversationMode`
preference, unchanged from today). The view renders only while this flag is `true`. Loading a
chat with Continuous as the saved preference does **not** set this flag — it starts `false`
regardless of the saved preference (resolved clarification).

**While active**: the view owns one `useConversationAudio` instance (`startTurn()` called on
entry), renders a full-presentation `SceneBackground` driven by that instance's own
`getReactiveIntensity`, and exactly two controls:
- **Exit**: calls the instance's `stop()`/`cancelListening()`, clears `isVoiceViewActive`, returns
  to the normal chat view. Does not change the persisted `conversationMode` preference.
- **Mute**: toggles the same mute mechanism (`voicePreferencesStore`'s persisted mute preference /
  `tts.isMuted`) already governing Lucy's spoken output elsewhere — not a new, independent mute
  concept.

**Not present in this view**: the text composer (message field, attach, insert-prompt, send,
mode-switch button) — matching FR-010.

**Superseded by this view once active** (research.md Decision 4): `ChatPage.tsx`'s previously
separate inline Continuous-mode wiring (`recognition` instance, `handleFinalTranscriptRef`, the
auto-start/mute-on-speaking effect) is removed — Continuous mode's actual listen/mute/respond loop
is now handled entirely by the dedicated view's own `useConversationAudio` instance, which already
implements a track-level `setInputMuted` mute (specs/033) rather than the old full-teardown
`recognition.cancel()`/`recognition.start()` approach.
