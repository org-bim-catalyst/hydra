# Research: Voice Controls & Composer Redesign

## Decision 1 — Root cause of the "confusing send to transcribe button"

**Finding**: `useVoiceRecorder.ts`'s `RecordingPhase` is `'idle' | 'recording' | 'reviewing' |
'transcribing'`. `RecordingReviewControls.tsx` renders a **Finish** (✓) button during `'recording'`
(calls `onFinish` → `recorder.finish()`, which stops the `MediaRecorder` and moves the phase to
`'reviewing'` — no transcription yet) and a separate **Accept** button during `'reviewing'`
(`RiSendPlane2Fill` icon, labeled "Send for transcription" — calls `onAccept` → `recorder.accept()`,
which is the call that actually transcribes). The Accept button's icon is *the same*
`RiSendPlane2Fill` icon `ChatComposer`'s real Send button uses — this is exactly the user-reported
"has same icon as send to transcribe." A hold-to-talk release also only calls `finish()`
(`ChatComposer`'s `onStopCapture` prop maps to `voiceControlsProps.onStop` = `recorder.finish`),
landing in the same `'reviewing'` state requiring the same manual second tap — so both gestures
(tap and hold) hit the identical confusing extra step today, not just one of them.

**Decision**: Collapse `'reviewing'` out of `RecordingPhase` entirely
(`'idle' | 'recording' | 'transcribing'`). Make `finish()` itself async: stop the recorder, await
the resulting blob, transition straight to `'transcribing'`, call the existing `transcribeAudio`
API, and return the transcript — merging what `finish()` + `accept()` did across two user actions
into one. `RecordingReviewControls.tsx` drops its `'reviewing'`-only Accept button entirely (dead
code once no phase ever rests at `'reviewing'`) and its `onAccept` prop. `ChatPage.tsx` replaces
the current split `onFinish: recorder.finish` / `recording.onAccept: () => void
handleRecorderAccept()` wiring with one handler (`handleFinishAndTranscribe`) that calls the new
async `finish()` and populates `composerText` with the result — the same append-not-replace logic
`handleRecorderAccept` already uses today, just triggered by one action instead of two.

**Rationale**: This single hook-level change fixes the flow for both gestures (FR-001/FR-002) at
once, satisfies "no additional confirmation... step" (FR-001/FR-003) exactly, and removes
genuinely dead code afterward rather than leaving an unreachable `'reviewing'` UI branch behind
(constitution §3 KISS/YAGN, §18 "never leave half-finished implementations").

**Alternatives considered**: Keeping `'reviewing'` as a phase and auto-invoking `accept()` from a
`useEffect` watching for the phase transition (Option (b) considered during planning) was
rejected — it's a side-effect-driven auto-chain that's harder to test/reason about and race-prone
compared to just making the single `finish()` call do the whole job synchronously-in-sequence.
Renaming `onFinish`/`finish()` to something like `onStopAndTranscribe` was considered for clarity
but rejected as unnecessary churn (YAGNI) — "finish recording" already accurately describes intent
from the caller's perspective; the auto-transcribe behavior is an implementation detail of what
"finishing" now does, not a new concept needing a new name.

## Decision 2 — Cancel stays exactly as-is; no new hold-cancel gesture

**Finding**: `RecordingReviewControls.tsx` already renders a functional Cancel button (→
`recorder.cancel()`) throughout any phase `!== 'idle'`, including `'recording'` — i.e., Cancel is
already reachable while a recording (tap- or hold-started) is actively in progress, today, with
zero new code. `recorder.cancel()` already handles both the `'recording'` case (stops the
in-progress `MediaRecorder`) and would have handled `'reviewing'` (now removed) — no change needed
to `cancel()` itself.

**Decision**: Make no changes to Cancel's implementation or reachability. Do not add a
drag-away/release-outside-bounds gesture. The only change relevant to Cancel is that the
`'reviewing'` window it used to also cover no longer exists (Decision 1) — Cancel now only ever
needs to cover the `'recording'` phase, which was already true.

**Rationale**: Reusing already-correct, already-tested behavior is the simplest option that
satisfies FR-004, and avoids inventing new pointer-tracking/gesture logic the user never asked
for (constitution §18 "never invent requirements not present in the approved specification" — an
earlier draft of this spec's Assumptions incorrectly proposed suppressing Cancel during a hold;
corrected before planning once the actual `RecordingReviewControls` code was read).

## Decision 3 — Decluttering the footer during an active Push-to-Talk recording

**Finding**: In `ChatComposer.tsx`'s current footer `Stack`, the mode-switch icon+menu, the
voice-preferences-unavailable indicator, and (pre-relocation) the mute-Lucy control all render
unconditionally, *alongside* the recording waveform + Finish/Cancel controls whenever
`isRecordingReview` is true — this simultaneous crowding (screenshots showed mic-waveform +
✓ + ✗ + blue "send for transcription" + fingerprint mode icon all visible at once) is the concrete
form of the "overwhelming" feedback, not the idle state (already reasonably minimal post-specs/030).

**Decision**: Wrap the attach button, insert-prompt button, mode-switch icon+menu, and the
voice-preferences-unavailable indicator in a single `{!isRecordingReview && (...)}` guard so they
disappear for the duration of an active recording, leaving only: the recording block (waveform +
Finish/Cancel, unchanged placement) and the Send button (always present, disabled while there's
no text yet — consistent with existing disabled-Send behavior). They reappear the instant
`isRecordingReview` goes false (recording finished/transcribed, or cancelled).

**Rationale**: This is the minimal structural change that directly matches the ChatGPT/Claude
reference behavior described (show only recording-relevant controls while recording) without
introducing a wholesale idle-state redesign the feedback didn't actually ask for — Continuous
mode's idle view was already confirmed correct as-is (User Story 4) and never shows a `recording`
block at all (only Push-to-Talk uses `useVoiceRecorder`), so it needs no equivalent change.

**Alternatives considered**: A full mode-aware sub-component split (e.g. separate
`PushToTalkVoiceControls`/`ContinuousVoiceControls` components) was considered, matching the
"mode-specific views" framing literally, but rejected as more restructuring than the actual
reported problem calls for — the crowding is specifically a *recording-state* problem, not an
idle-state one, and a conditional guard within the existing component is the simplest fix
satisfying FR-006/FR-008 (KISS, constitution §3).

## Decision 4 — Translate removal scope

**Finding**: `sendTranslation` (in `useChatStream.ts`) is called from exactly one place —
`ChatPage.tsx`'s `handleTranslateLast`, which is the sole caller of `sendTranslation` across the
codebase (verified via project-wide search: only `useChatStream.ts`'s own definition and
`ChatPage.tsx` reference the name).

**Decision**: Remove `onTranslateLastClick`/its `Tooltip`+`IconButton`+`RiTranslate2` import from
`ChatComposer.tsx` and its `ChatComposerProps` interface; remove `handleTranslateLast` and the
`onTranslateLastClick={handleTranslateLast}` wiring from `ChatPage.tsx`; remove `sendTranslation`
from `useChatStream.ts`'s implementation and its returned object, since it becomes fully unused
once its one call site is gone.

**Rationale**: Per spec.md FR-010 ("removed entirely... not reachable from any other control") and
constitution's "if you are certain something is unused, delete it completely" — leaving
`sendTranslation` in `useChatStream.ts` unused would be exactly the kind of dead code the project's
conventions forbid.

## Decision 5 — Mute/unmute-Lucy relocation

**Finding**: `ChatComposer.tsx` currently owns the mute Tooltip+IconButton (`isMuted`,
`onToggleMute` props, `RiVolumeMuteLine`/`RiVolumeUpLine`). `ExpandedChatPanel.tsx`'s header
already accepts new props cleanly (specs/030-composer-panel-refinements added `isFullHeight`/
`onToggleHeight` the same way) — the same pattern applies here. `ChatPage.tsx` already computes
`isMutedPreference`/`handleToggleMute` at the `ConversationView` level, available to wire into
either component.

**Decision**: Remove the mute `Tooltip`+`IconButton` block from `ChatComposer.tsx`'s footer and
its `isMuted`/`onToggleMute` props from `ChatComposerProps`. Add `isMuted: boolean` /
`onToggleMute: () => void` to `ExpandedChatPanelProps`, and render a `Tooltip`+`IconButton`
immediately after the name/status `Box` and before `ActiveLanguageFlag` in the header `Stack`
(same relative position the user specified — "to the right of the picture," reading Lucy's
portrait+name+status as one identity block the mute control sits beside). `ChatPage.tsx` rewires
`isMutedPreference`/`handleToggleMute` from the `ChatComposer` instance to the `ExpandedChatPanel`
instance.

**Rationale**: Mirrors the exact prop-threading pattern specs/030-composer-panel-refinements
already established for `isFullHeight`/`onToggleHeight`, keeping the codebase's convention
consistent (constitution §7 "Convention over Configuration").

## Decision 6 — Attach-file format investigation (FR-013)

**Finding**: `ChatComposer.tsx`'s file input already declares `accept=".pdf,.csv,audio/*"`, and
`handleFile` already branches correctly: `application/pdf` → `usePdfTextExtraction`'s client-side
`pdfjs-dist` extraction (no server call), `audio/*` → `transcribeAudio` (a real `POST
/ai/transcriptions` call), `text/csv`/`.csv` → plain text read. All three are real, wired
implementations, not stubs. The "Transcription failed with 500" screenshot's error string
(`Transcription failed with ${response.status}`) is produced by `aiApi.ts`'s `transcribeAudio`
verbatim — confirming it's a genuine backend/runtime failure of the `/ai/transcriptions` endpoint,
not a client-side defect.

**Decision**: No code change to `accept`/`handleFile`'s dispatch logic — it already does what FR-013
requires. The likely explanation for "attach only supports audio" is a perception/labeling issue
(a native multi-type `accept` attribute's browser-rendered file-picker filter dropdown can default
to showing one type's label, e.g. "Audio Files," even though PDF/CSV are also selectable via an
"All Files" or similar option) rather than a functional gap — this is standard, OS-controlled
`<input type="file" accept="...">` picker behavior, not something this component's code
independently controls. No fix is applied beyond confirming and documenting this in
`quickstart.md` as a verification step; the reported backend 500 is flagged as a separate,
out-of-scope follow-up per spec.md's Assumptions.

**Rationale**: Building anything further here would be inventing work the investigation shows
isn't needed (constitution §18) — the two features the user set as the bar ("if these two
features are in place then this is fine") are already both genuinely implemented.

## Decision 7 — `CollapsedVoiceControls.tsx` is not directly edited

**Finding**: `CollapsedVoiceControls.tsx` renders `RecordingReviewControls` with the exact same
props shape as `ChatComposer.tsx`, sharing the same `VoiceControlsProps`/`recording` contract
(`research.md #10` from specs/026-floating-chat-assistant, still true today). Decision 1's fix is
made inside `RecordingReviewControls.tsx` and `useVoiceRecorder.ts` — both consumed by
`CollapsedVoiceControls.tsx` unchanged.

**Decision**: Do not modify `CollapsedVoiceControls.tsx`. Re-run its existing test suite as a
regression check (it should still pass — its own tests don't assert on `'reviewing'`-phase-specific
UI beyond what `RecordingReviewControls`'s own tests already cover) rather than editing it.

**Rationale**: The bug this feature fixes is a shared-component bug; fixing it once and verifying
the second consumer still passes is the correct scope — editing a file with no required change
would be scope creep past what spec.md's User Stories (all scoped to `ChatComposer`/
`ExpandedChatPanel`) call for.
