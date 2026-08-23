# Quickstart: Validating SPEC-033

## Prerequisites

- Backend running locally with a valid OpenAI API key, or `OpenAIProviderTests` run against a
  mocked handler (no live key needed for automated validation).
- Frontend dev server running against the local backend.
- Browser with microphone permission granted; device speakers (not headphones) for Scenario 3.

## Scenario 1 — Transcription reliability & deployment discipline (User Story 1, P1)

1. Record a short, real spoken message via Push-to-Talk in the Expanded panel.
   - **Expected**: transcript appears in the message field. No generic failure.
2. (Automated) `OpenAIProviderTests`: a mocked 2xx response with an empty/malformed body throws
   `AiProviderUnavailableException`; existing 400/401/403/429/500 classification from SPEC-032 is
   unchanged.
3. (Process) Confirm via `git log`/the merged PR that this feature's changes (and SPEC-032's, if
   not already separately shipped) are committed to `main` and the production deployment reflects
   that commit — not just present in a local working copy.

## Scenario 2 — Pure hold-to-talk (User Story 2, P1)

1. In the Expanded panel, press and hold the mic button, speak for ~1 second, release.
   - **Expected**: recording is active only while held; releasing immediately transcribes into the
     message field. No "Finished speaking" button appears or is needed.
2. Press and release the mic very quickly (a brief tap).
   - **Expected**: same press-then-release behavior — a brief recording, transcribed on release —
     not a recording left running.
3. Record something you don't want to send.
   - **Expected**: the transcript still lands in the message field as editable draft text; delete
     it or don't press Send. No separate mid-recording Cancel button appears.
4. In the Collapsed (floating) widget, click the mic once, then click Finish or Cancel.
   - **Expected**: unchanged from before this feature — this surface's click-to-toggle flow with
     Finish/Cancel buttons is untouched.
5. (Automated) `ChatComposer.test.tsx`: pointerdown-then-immediate-pointerup transcribes (no
   threshold gating); a held pointerdown-then-later-pointerup also transcribes; no
   `RecordingReviewControls` (Finish/Cancel) renders during a Push-to-Talk recording in
   `ChatComposer`.

## Scenario 3 — No self-listening in Continuous mode (User Story 3, P2)

1. Start a Continuous-mode conversation using device speakers at normal volume.
2. Let Lucy speak a full response.
   - **Expected**: her own voice is not picked up as user speech; no spurious interruption/reaction
     occurs during her reply.
3. Once she finishes, speak normally.
   - **Expected**: the mic resumes listening promptly, with no noticeable added delay.
4. (Automated) `useConversationAudio.test.ts`: entering `'AiSpeaking'` calls
   `recognition.setInputMuted(true)`; leaving it calls `setInputMuted(false)`; no
   `'Interrupted'` state transition occurs at any point.

## Success criteria mapping

| Scenario | Validates |
|---|---|
| 1 | SC-001, SC-003 |
| 2 | SC-002, SC-005 |
| 3 | SC-004 |
