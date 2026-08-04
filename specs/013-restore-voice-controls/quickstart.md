# Quickstart: Restore Voice Output Mute & Input Mode Controls

Manual/E2E validation guide once implementation (tasks.md) is complete. No new backend
setup is required — this feature reuses spec 012's existing ElevenLabs/browser-fallback
voice engine and `/api/v1/ai/voice/*` endpoints as-is.

## Prerequisites

- Backend running with the existing ElevenLabs (or fallback) voice provider configured, as
  already required for `012-elevenlabs-voice-engine` — see
  [../012-elevenlabs-voice-engine/quickstart.md](../012-elevenlabs-voice-engine/quickstart.md)
  for provider setup if not already running locally.
- Frontend dev server running (`npm run dev` in `src/AskLucy.Web/ClientApp`).
- A logged-in test user with an existing chat conversation.
- A browser with microphone permission promptable (Chrome/Edge recommended — matches the
  `AudioWorkletNode`/`MediaSource` APIs `useSpeechRecognition`/`useVoiceAnalyzer` depend on).

## Scenario 1 — Mute during playback (US1, SC-001/SC-002)

1. Open a chat, send a message that triggers a spoken reply.
2. While Lucy is speaking, click the mute control in `VoiceControlBar`.
   **Expect**: audio stops within ~1 second; the reply's text keeps streaming/finishes
   normally; the reactive sphere keeps animating (visualization unaffected by mute).
3. Wait for the reply to finish, then send another message.
   **Expect**: the new reply is not spoken (still muted); no audio plays retroactively for
   the reply muted in step 2.
4. Click unmute.
5. Send a third message.
   **Expect**: this reply is spoken aloud normally. The reply from step 3 is never played
   back.

## Scenario 2 — Keyboard-only mute (US1, SC-005)

1. Using Tab/Shift+Tab only (no mouse), focus the mute control.
2. Activate it with Enter/Space.
   **Expect**: same mute behavior as Scenario 1, fully operable without a pointer.

## Scenario 3 — Push-to-talk, hold activation (US2)

1. Set input mode to Push-to-Talk (default).
2. Press and hold the mic control (mouse down or touch start), speak, then release.
   **Expect**: capture starts on press, stops on release, and the utterance is transcribed
   and sent as the next turn.
3. Repeat using the bound keyboard shortcut (press-and-hold Space) instead of pointer/touch.
   **Expect**: identical behavior via keyboard alone.

## Scenario 4 — Push-to-talk, toggle activation (US2, Clarification Q1)

1. Click/tap the mic control once (do not hold).
   **Expect**: capture starts and stays active without holding.
2. Click/tap it again.
   **Expect**: capture stops and the utterance is processed — identical outcome to the
   hold path in Scenario 3.

## Scenario 5 — Continuous listening (US2)

1. Switch input mode to Continuous Conversation.
2. Speak without touching any control.
   **Expect**: speech is captured, processed after a pause, and the mic remains ready for
   the next utterance without re-activation.
3. While continuous listening is active, type a message in the text composer instead.
   **Expect**: the microphone keeps listening (per Clarification Q3); typing and speaking
   are independent — sending the typed message does not disable continuous listening.

## Scenario 6 — Mode switch takes effect immediately, except mid-capture (US2, Clarification Q4)

1. In Push-to-Talk mode, with no capture in progress, switch to Continuous.
   **Expect**: switch happens immediately; conversation history/context is untouched.
2. Switch back to Push-to-Talk, then press-and-hold the mic control to start a capture.
   While still holding, attempt to switch to Continuous mode.
   **Expect**: the mode toggle is disabled (or the attempted switch is rejected) until the
   hold is released; releasing re-enables the switch immediately.

## Scenario 7 — Permission denied (US2, FR-009)

1. In a fresh browser profile (or after revoking mic permission for the site), select
   Continuous Conversation or attempt a push-to-talk capture.
   **Expect**: a browser permission prompt appears; if denied, a specific, visible,
   actionable error message is shown — not a silent no-op.

## Scenario 8 — Preferences persist across sessions (US1 + US2, SC-004)

1. Mute audio output and switch to Continuous mode.
2. Reload the page (or log out and back in).
   **Expect**: mute state and Continuous mode are restored automatically without
   reconfiguration (`GET /api/v1/ai/voice/preferences` hydration on load).

## Automated coverage this quickstart maps to

- `VoiceControlBar.test.tsx` — extend for hold vs. toggle activation and the mode-switch
  guard (Scenarios 3, 4, 6).
- `useVoiceOutput.test.ts` (new) / extended — mute gates `speak()`, `stop()` fires on
  mute-while-playing, no retroactive playback on unmute (Scenario 1).
- `useSpeechRecognition.test.ts` — existing continuous-mode silence-commit behavior verified
  against Scenario 5's typing-independence; permission-denied path verified against
  Scenario 7.
- `ChatComposer.test.tsx` (new or extended) — push-to-talk fills the text field without
  auto-send vs. continuous mode auto-sends (Scenarios 3–5).
- jest-axe on `VoiceControlBar.tsx` (existing pattern) — covers Scenario 2's keyboard
  operability at the component level.
