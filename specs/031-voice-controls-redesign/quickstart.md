# Quickstart: Validating Voice Controls & Composer Redesign

## Prerequisites

- `src/AskLucy.Web/ClientApp` dependencies installed.
- A runnable dev environment (backend + Vite) for manual browser verification — recording/
  transcription and real MUI Tooltip/focus behavior can't be fully exercised in jsdom alone.
- A working `/ai/transcriptions` backend endpoint for the manual voice scenarios (the "500" error
  reported in testing is a known separate issue — if still present, scenarios 1–2 below will
  correctly show FR-015's error path instead of a transcription, which is itself worth confirming).

## Automated checks

From `src/AskLucy.Web/ClientApp`:

```bash
npm run test -- ChatComposer ExpandedChatPanel useVoiceRecorder RecordingReviewControls CollapsedVoiceControls ChatPage useChatStream
npx tsc --noEmit
npm run lint
```

Expected: all pass, zero new TypeScript errors, zero new lint violations on changed files.

## Manual validation scenarios (map to spec.md's User Stories)

1. **US1 — tap-then-finish**: In Push-to-Talk mode, tap the mic once, speak a short sentence, tap
   Finish (✓). Confirm no intermediate "send to transcribe" control ever appears — the transcribed
   text lands directly in the message field, editable. Confirm the composer's normal Send button
   is the only next action, and pressing it sends the message.

2. **US2 — hold-and-release**: In Push-to-Talk mode, press and hold the mic, speak, release.
   Confirm recording stops the instant you release and the transcribed text appears in the field
   immediately, with no extra tap. Confirm Send works the same as scenario 1.

3. **US1/US2 — Cancel**: Start a recording (either gesture), tap Cancel before finishing/releasing.
   Confirm the message field is left exactly as it was (no transcription inserted).

4. **US3 — declutter while recording**: Start a Push-to-Talk recording. Confirm the attach,
   insert-prompt, and mode-switch icons disappear for the duration of the recording, leaving only
   the waveform, Finish, Cancel, and Send visible. Confirm they reappear once the recording ends
   (transcribed or cancelled).

5. **US4 — Continuous mode unaffected**: Switch to Continuous mode. Confirm the mic icon still
   simply starts/stops listening on tap, exactly as before this feature.

6. **US5 — translate gone**: Open the composer in any state. Confirm no translate icon/button
   appears anywhere in the footer, with or without a prior assistant response in the conversation.

7. **US6 — mute relocated**: Open the expanded panel. Confirm the mute/unmute-Lucy control appears
   in the header immediately next to Lucy's portrait/name, and is no longer present in the
   composer footer. Trigger a spoken response, then mute — confirm playback stops immediately and
   the toggle behaves exactly as before relocation.

8. **FR-013 — attach formats**: Attach a PDF (confirm extracted text appears in the field), a CSV
   (confirm its text appears), and an audio file (confirm transcription appears, assuming the
   backend endpoint is healthy). Note whether the native file-picker's filter dropdown makes all
   three types easy to find, or whether it's still confusing — report back rather than silently
   guessing at a fix beyond what research.md's Decision 6 already covers.

9. **Regression pass**: Confirm every specs/029/specs/030 composer/panel behavior not touched by
   this feature (rounded-rectangle shape, capped textarea growth, panel full-height toggle and its
   placement/persistence, remaining tooltips) is unchanged.

## Expected outcome

All nine scenarios pass, with scenario 8 specifically reported back (not silently fixed further)
per research.md Decision 6's scope boundary.
