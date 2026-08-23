# Quickstart: Validating SPEC-032

## Prerequisites

- Backend running locally (`AskLucy.Web`) with a valid OpenAI API key configured, or with
  `OpenAIProviderTests` run against a mocked `HttpMessageHandler` (no live key needed for
  automated validation).
- Frontend dev server running (`AskLucy.Web/ClientApp`) against the local backend.
- Browser with microphone permission granted to the app.

## Scenario 1 — Reliable transcription (User Story 1, P1)

1. Open the chat widget, ensure Push-to-Talk mode is active.
2. Tap the mic once, speak a short sentence, tap "Finish".
   - **Expected**: transcript text appears in the message field. No "Transcription failed with
     500" (or any generic failure) appears.
3. Press-and-hold the mic, speak, release.
   - **Expected**: same as step 2 — transcript populates immediately on release.
4. (Automated equivalent) Run `OpenAIProviderTests`: assert that a mocked 400 response from
   OpenAI's transcription endpoint results in `AiProviderRequestInvalidException` being thrown
   (not a bare `HttpRequestException`), and that a mocked 401/403/429/500 response still produces
   the existing, unchanged exception types.
5. (Automated equivalent) Run the new middleware mapping test: assert
   `AiProviderRequestInvalidException` maps to HTTP 400 with a non-empty `detail` in the Problem
   Details body.
6. (Automated equivalent) Run `aiApi`/`useVoiceRecorder` Vitest suites: assert that a mocked 400
   JSON Problem Details response causes `useVoiceRecorder`'s `error` state to contain the
   response's `detail` text, not a bare status-code string.

## Scenario 2 — Single-click mode switch (User Story 2, P2)

1. With the composer idle in Push-to-Talk mode, click the mode-switch icon once.
   - **Expected**: mode immediately becomes Continuous. No dropdown/menu ever appears.
2. Click the icon once more.
   - **Expected**: mode immediately reverts to Push-to-Talk. No dropdown/menu appears.
3. Start a Push-to-Talk recording (press-and-hold, don't release yet); attempt to click the
   mode-switch icon.
   - **Expected**: icon remains disabled/inert, exactly as before this fix.
4. (Automated equivalent) Run `ChatComposer.test.tsx`: assert a single `click` (or
   `userEvent.click`) on the mode-switch button calls `onToggleMode` directly with no `Menu`
   rendered at any point, and that the button is disabled when recording is in progress.

## Scenario 3 — Hold-to-talk gesture regression (User Story 3, P3)

1. In Push-to-Talk mode, press and hold the mic button while speaking; release.
   - **Expected**: recording stops the instant the button is released; transcript appears in the
     message field with no extra tap.
2. Review the transcribed text, tap Send.
   - **Expected**: message sends normally, exactly as in specs/031.
3. (Automated equivalent) Re-run the existing `useVoiceRecorder.test.ts` /
   `pages/ChatPage.test.tsx` hold-gesture test cases from specs/031 — all must still pass unchanged
   (no new assertions required; this is a pure regression check per spec.md User Story 3).

## Success criteria mapping

| Scenario | Validates |
|---|---|
| 1 | SC-001, SC-002 |
| 2 | SC-003 |
| 3 | SC-004 (hold gesture + FR-005 unavailable/rate-limited paths untouched) |
