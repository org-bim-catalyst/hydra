# Research: Hold-to-Talk Simplification & Self-Listening Fix

## Decision 1 — Close the remaining "malformed 2xx response" gap in `TranscribeAudioAsync`

**Finding**: `OpenAIProvider.TranscribeAudioAsync` (`:187-206`), after `EnsureSuccessAsync` confirms
a 2xx status, does `JsonDocument.ParseAsync(stream)` then `document.RootElement.GetProperty("text")`
with **no exception handling at all**. A malformed, empty, or unexpected-shape response body from
OpenAI (a genuine, if rare, provider-side failure mode — not a status-code problem, so SPEC-032's
fix does not cover it) throws `JsonException` (parse failure) or `KeyNotFoundException`
(missing `text` property, confirmed by implementation — `JsonElement.GetProperty` throws this, not
`InvalidOperationException` as originally assumed here; `InvalidOperationException` is still worth
catching too, for the case where `text` is present but not a string), neither of which
`ProblemDetailsMiddleware.Map()` has a case for — both fall into the generic `_ => 500` default.
This is a real, previously-unidentified second cause of the exact symptom SPEC-032 was meant to
fix, and is plausible enough (alongside "SPEC-032 was never committed/deployed," Decision 2) to
explain the still-reproducing production 500.

**Decision**: Wrap the parse-and-extract block in a `try/catch` for `JsonException` and
`InvalidOperationException` (the exception `JsonElement.GetProperty` throws when the property is
absent), and rethrow as `AiProviderUnavailableException("The AI service returned an unexpected
response. Please try again.")` — reusing the existing sibling exception type (already mapped to
502 by `ProblemDetailsMiddleware`) rather than inventing a new one. This is *not* the client's
fault (unlike SPEC-032's `AiProviderRequestInvalidException` for a rejected request), so 502
("upstream problem, retry") is the correct classification, not 400.

**Rationale**: Reuses an existing exception type/mapping (constitution §3 DRY) rather than adding
a fourth `AiProvider*Exception` variant for what is conceptually the same "the provider didn't give
us something usable" condition `AiProviderUnavailableException` already represents. No retry is
added for this specific catch (a malformed body is unlikely to differ on an immediate blind retry,
and simplicity favors surfacing it immediately over adding a second, redundant retry path
alongside `WithRetryAsync`'s existing one).

## Decision 2 — This round's fix must actually ship: commit, merge, and verify deployment

**Finding**: `git log` confirms SPEC-032's changes to `OpenAIProvider.cs` (and every other file it
touched) were never committed — they have existed only as uncommitted local working-tree changes
this entire time. The user's "I published from VS2026 and yet I still get error" report cannot be
confidently attributed to either "the fix isn't deployed" or "the fix is deployed but insufficient
(Decision 1's gap)" without first eliminating the uncommitted-code risk entirely.

**Decision**: This feature's own `/speckit-cicd` pass (already required by the established
per-feature workflow) is treated as a **functional requirement of this fix** (FR-004/SC-003), not
just routine process — it must actually run to completion (commit → push → PR → merge → deploy
verification) before this feature is considered done, specifically so "was it actually deployed?"
can never again be an open question for a bug-fix round.

**Rationale**: Directly closes the process gap the user's report exposed. No code change follows
from this decision by itself — it's a delivery-discipline requirement threaded through tasks.md's
Polish phase and this feature's completion criteria.

## Decision 3 — Push-to-Talk becomes pure hold-to-talk; root-causes and fixes the actual gesture bug

**Finding**: `ChatComposer.tsx`'s current dual-gesture design (`handleMicPointerDown`/
`handleMicPointerUp`, `HOLD_THRESHOLD_MS = 350`) is not just being replaced because the user
dislikes it — it has a genuine, previously-flagged-but-unfixed bug (noted in this session's own
prior work: "React's async `start()`-then-DOM-swap timing... the plain mic `IconButton` (owning
`onPointerUp`) [gets swapped] for `RecordingReviewControls` (Finish/Cancel), often before a real
hold's release fires"). Root cause, now fully traced: `isRecordingReview = Boolean(recording) &&
recording?.phase !== 'idle'` flips true the instant `recorder.start()` transitions `phase` to
`'recording'` — which happens asynchronously very soon after `pointerdown`, well before the user's
physical release. Because `handleMicPointerDown` never calls `event.currentTarget.
setPointerCapture(event.pointerId)`, the browser has no explicit pointer capture in effect; once
the mic `IconButton` unmounts (replaced by the waveform + `RecordingReviewControls` block) and a
*different* DOM element occupies that screen position, the subsequent native `pointerup` routes to
whatever element is now there — not to the original mic button — so `handleMicPointerUp` never
fires for a real hold. In practice this means **every real physical hold in production already
behaves as if it were a quick tap**: capture starts and is silently left running, matching the
user's exact complaint ("it doesn't allow me to keep pressing or holding... it considers this as a
click and start recording") and explaining why they had to separately tap "Finished speaking"
(where SPEC-032's underlying 500 was then hit).

**Decision**:
1. Remove `HOLD_THRESHOLD_MS`, `suppressNextClickRef`, and the tap-vs-hold duration logic in
   `handleMicPointerDown`/`handleMicPointerUp`/`handleMicKeyDown`/`handleMicKeyUp`/
   `handleMicClick` entirely. Replace with: `pointerdown`/`keydown(Space)` always calls
   `onStartCapture()`; `pointerup`/`pointerleave`/`pointercancel`/`keyup(Space)` always calls
   `onStopCapture()` (which, per `ChatPage.tsx`'s wiring, already *is*
   `handleFinishAndTranscribe` — no change needed to what it does, only when it's called).
   **Correction made during implementation**: the up-handlers must gate on a local,
   synchronously-set ref (`isCapturingRef`, repurposing `captureStartedAtRef`'s mechanism rather
   than removing it) — not on the `isListening` *prop* — because `isListening` only updates once
   the parent re-renders after `onStartCapture()`'s async effects propagate back down. A fast
   real release can fire before that round-trip completes, which would silently drop the stop
   call if the guard depended on the prop — reintroducing the same class of async-timing bug
   this feature exists to fix, just moved from the render layer to the guard condition.
2. Call `event.currentTarget.setPointerCapture(event.pointerId)` in the pointerdown handler — this
   is the actual fix for the root-caused bug: it guarantees every subsequent pointer event for
   that press (move/up/cancel) routes to the same element for the duration of the gesture,
   regardless of any DOM changes elsewhere on the page.
3. Stop rendering `RecordingReviewControls` (the Finish ✓ / Cancel ✗ buttons) inside
   `ChatComposer.tsx` during a Push-to-Talk recording — with pure hold-to-talk, the mic button
   itself is the entire interaction (press/release); a separate Finish button is redundant, and
   (per the resolved clarification) Cancel is no longer offered as a pre-send affordance in this
   surface. The mic `IconButton` stays mounted throughout — recording start, recording, and the
   brief `'transcribing'` phase all render as visual state changes (color/icon/disabled) on the
   *same* element, never a swap to different components. The live waveform (`VoiceAnalyzer`)
   continues to render alongside it, unchanged.

**Scope correction found during this research** (important — affects blast radius): `RecordingReviewControls`
is a *shared* component also used by `CollapsedVoiceControls.tsx` (the floating collapsed
widget), confirmed via `ChatPage.tsx:520-543` — but that surface's Push-to-Talk mic is a **plain
click-to-toggle** (`handleMicClick`: click starts, a later click on the *same* button stops),
structurally unrelated to the hold-gesture bug and with no tap/hold ambiguity at all. It reads
`recording.onCancelRecording`/`recording.onFinish` from the exact same shared `voiceControlsProps`
object `ChatComposer` uses (`recorder.cancel`/`handleFinishAndTranscribe` respectively) — nothing
about those functions changes. **`RecordingReviewControls.tsx`, `CollapsedVoiceControls.tsx`,
`useVoiceRecorder.ts`'s `cancel()`, and `ChatPage.tsx`'s `voiceControlsProps` wiring are therefore
all left untouched** — the Collapsed widget keeps its existing, still-fully-valid Finish/Cancel
button flow unchanged; only `ChatComposer.tsx`'s gesture-handling and rendering change. The
resolved clarification ("drop the dedicated Cancel affordance") applies specifically to the
Expanded panel's hold gesture, where it's genuinely unreachable under the new rule — not
project-wide.

**Rationale**: Fixes a real, previously-identified, root-caused bug rather than merely changing
stated behavior; `setPointerCapture` is the standard, minimal-footprint web-platform mechanism for
exactly this class of gesture (a control whose visual state may change mid-press must not lose the
in-flight pointer sequence). Scoping the Cancel-removal correctly (Expanded panel only) avoids an
unintended regression to the Collapsed widget's independently-correct, unrelated flow.

## Decision 4 — Mute the microphone input during Lucy's speech in Continuous mode

**Finding**: `useSpeechRecognition.ts`'s audio worklet (`:273-298`) computes each chunk's peak
amplitude and forwards *every* chunk to the ElevenLabs WebSocket (`input_audio_chunk`) regardless
of the current conversation phase; `useConversationAudio.ts`'s doc comment confirms this is
deliberate — "kept connected through the `AiSpeaking` phase... so the same live audio feed... also
drives the local amplitude-threshold interruption pre-trigger" (specs/031 research.md Decision 10,
User Story 3 "natural interruption"). `getUserMedia` is called with a bare `audio: true` (or a
plain `{deviceId: {...}}`) constraint — no explicit `echoCancellation: true` — and nothing disables
the raw `MediaStreamTrack` during playback; only a **soft duck of Lucy's own output** happens if
the local pre-trigger (`LOCAL_SPEECH_RMS_THRESHOLD = 0.02`) fires. On speaker hardware without
strong acoustic echo cancellation, Lucy's own voice leaking into the mic can cross that threshold
and be misread as the user speaking — exactly the reported self-listening symptom.

**Decision** (per the resolved clarification — full mute, interruption intentionally removed):
1. `useSpeechRecognition.ts` exposes a new `setInputMuted(muted: boolean)` function that toggles
   `streamRef.current?.getAudioTracks().forEach(t => { t.enabled = !muted })`. A disabled
   (`enabled = false`) `MediaStreamTrack` delivers silence to the worklet without tearing down or
   reconnecting the `AudioContext`/`AudioWorkletNode`/WebSocket — the existing "kept connected"
   architecture is preserved structurally; only the *content* of what flows through it changes.
2. `useConversationAudio.ts` calls `recognition.setInputMuted(true)` when `voiceState.state`
   transitions to `'AiSpeaking'`, and `setInputMuted(false)` when it transitions away from
   `'AiSpeaking'` (e.g., in `runAssistantTurn`'s `onDone`/post-synthesis path and in `stop()`).
3. **Remove the now-dead interruption/ducking machinery**, since a muted input track will never
   cross the RMS threshold during `AiSpeaking` and so can never fire `onLocalSpeechLikely` during
   the only phase that mechanism's guard (`voiceState.state !== 'AiSpeaking'` early-return) allows
   it to do anything: `handleLocalSpeechLikely`, `isDuckedRef`, `duckTimeoutRef`,
   `clearDuckTimeout`, the `'Interrupted'` voice-state transition and its 1500ms false-positive
   timeout, and the `onLocalSpeechLikely` prop passed to `useSpeechRecognition`. Confirmed via
   grep that `'Interrupted'` has no other UI consumer (only `useVoiceState.ts` itself and
   `useConversationAudio.ts`/its test reference it) — safe to remove without touching rendering
   elsewhere. `useVoiceState.ts`'s `VoiceStateName` union keeps `'Interrupted'` as a type (removing
   a public state name is a larger, unrelated cleanup) but nothing sets it anymore.
4. Add `echoCancellation: true` to the `getUserMedia` audio constraint as a small, low-risk
   defense-in-depth improvement for the (now much smaller) window where the mic is live near
   Lucy's own audio — even though full muting is the primary fix, requesting echo cancellation
   explicitly (rather than relying on a browser's default) costs nothing and is already
   industry-standard practice for any two-way audio scenario.

**Note found during implementation**: `runAssistantTurn`'s `if (voiceState.state !== 'AiSpeaking')`
guard (pre-existing, unchanged by this feature) checks a closure snapshot of the zustand store
taken at the callback's creation, not a live read — so in practice it evaluates `true` on every
`onAudioChunk` within a turn, not just the first. This means `recognitionSetInputMutedRef.current(true)`
(added alongside the existing `voiceState.setState('AiSpeaking')` call this guard already
wrapped) fires once per chunk rather than exactly once. This is harmless — `setInputMuted(true)`
is idempotent (repeatedly disabling an already-disabled track is a no-op) — and pre-dates this
feature, so it was left as-is rather than fixed as part of this change; the test suite asserts the
honest behavior (every pre-turn-completion call is `true`, the final call is `false`) rather than
an exact call count.

**Rationale**: Directly implements the resolved clarification. Toggling `track.enabled` is the
minimal-diff mechanism available — it doesn't require restarting the audio graph, socket, or
reconnecting to ElevenLabs (avoiding reconnect latency/cost on every single AI turn in a
back-and-forth conversation), and reuses the exact mechanism the browser platform provides for
exactly this purpose. Removing the dead ducking code (rather than leaving it inert) follows
constitution §3 (no dead code) and avoids a future maintainer misreading disabled-but-present
interruption logic as still-active behavior.

## Decision 5 — No new backend test-file placement conflict this round

**Finding**: This feature's backend-relevant files (`OpenAIProvider.cs`) are already dirty from
SPEC-032 (still uncommitted) plus, per specs/032 research.md Decision 6, one pre-existing unrelated
line. `tests/AskLucy.Infrastructure.Tests/Ai/OpenAIProviderTests.cs` is a **new** file created
during SPEC-032's own implementation (also still uncommitted) — extending it further for this
round's `TranscribeAudioAsync` malformed-response test is safe, since it's this session's own file,
not a pre-existing-dirty one.

**Decision**: Add the new malformed-response test case directly to the existing
`OpenAIProviderTests.cs` rather than creating another new file — no placement conflict exists here
the way specs/032 research.md Decision 5 found for other files.

**Rationale**: Consistent with the established convention of only avoiding edits to files with
*unrelated* pre-existing dirt — this file's existing dirt (SPEC-032's own uncommitted work) is
directly related and will be committed together with this feature's changes anyway.
