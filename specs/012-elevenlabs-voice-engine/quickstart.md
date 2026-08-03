# Quickstart: Validating the ElevenLabs Conversational Voice Engine

**Feature**: [spec.md](./spec.md) | **Data model**: [data-model.md](./data-model.md) |
**Contracts**: [contracts/](./contracts/)

Manual/scripted validation scenarios proving the feature works end-to-end, mapped to the
spec's user stories and success criteria. Run after implementation, before marking the
feature done (constitution §19 Definition of Done).

## Prerequisites

- Solution built and running locally (`dotnet run` for `AskLucy.Web`), against a local SQL
  Server instance with this feature's migration (`AddVoiceEngineTables`) applied.
- A real ElevenLabs API key configured in `ElevenLabsOptions` (user secrets/environment, per
  constitution §8 — never in `appsettings.json`). A **second, deliberately invalid** key
  (or a way to point `ElevenLabsOptions.BaseUrl` at an unreachable host) is needed for the
  fallback scenarios below.
- A logged-in test user, and a second user with the `Administrator` role (existing auth flow).
- A working microphone and speakers/headphones on the test machine — this feature cannot be
  meaningfully validated headless; browser automation with a virtual audio device is a
  `/speckit-tasks`-time decision for CI, not covered here.
- A Chromium-based browser (for `AudioWorkletNode`/`getUserMedia` parity with the existing
  `useWavRecorder.ts` requirements already in production).

## Scenario 1 — Push-to-Talk with natural, streaming speech (User Story 1 / SC-001)

1. Open a conversation, click the microphone (Push-to-Talk mode, the default). Confirm the
   mic visibly enters the "listening" state (FR-020).
2. Speak a short sentence. Confirm a transcript appears without needing to click anything to
   signal "done" (FR-002), and the mic returns toward "processing" automatically.
3. Confirm the AI's spoken reply **begins audibly playing before the on-screen text has
   finished streaming** — time it: audio should start within 2 seconds of the first text
   delta (SC-001), not after the `done` event.
4. Confirm the voice sounds like a natural ElevenLabs voice, not the browser's previous
   robotic default, and confirm the sphere visualization is visibly reacting to the AI's
   voice in real time, not on a fixed idle loop.
5. Let the reply finish; confirm the mic returns to idle (FR-013) and requires another click
   to speak again.

**Pass condition**: matches spec.md User Story 1's four acceptance scenarios; SC-001 verified
by timing the gap between first text delta and first audible sound.

## Scenario 2 — Continuous Conversation Mode, hands-free (User Story 2 / SC-003, SC-004)

1. Toggle Conversation Mode to "Continuous" (FR-015) — confirm the conversation itself is
   unaffected (same chat, same history visible).
2. Speak without clicking anything; confirm the mic detects speech start automatically
   (FR-014) and, after a natural pause, stops capturing and begins processing on its own
   (FR-002).
3. Let the AI's reply play fully. Time how long it takes for the mic to resume listening
   after the audio finishes — should be under 1 second with zero clicks (SC-003).
4. Repeat for at least three consecutive turns with no manual interaction at all beyond the
   initial mode toggle (SC-004).
5. Explicitly exit voice mode; confirm the automatic loop stops immediately (FR-014
   acceptance scenario 4).

**Pass condition**: matches User Story 2's four acceptance scenarios; SC-003/SC-004 verified
by timing and by confirming zero required clicks across the multi-turn run.

## Scenario 3 — Natural interruption (User Story 3 / SC-002)

1. In either mode, ask a question that produces a longer spoken reply.
2. While the AI is still speaking, start talking. Time the gap between when you start
   speaking and when AI audio stops — should be under 300ms in at least 95% of repeated
   trials (SC-002).
3. Confirm no further audio from the interrupted reply plays afterward (FR-019), and confirm
   the system immediately begins capturing your new speech without needing a separate stop
   action first (FR-018).
4. Finish speaking your new message; confirm the AI responds to the **new** message, not a
   continuation of the interrupted one.

**Pass condition**: matches User Story 3's three acceptance scenarios; SC-002 verified across
multiple repeated interruption trials.

## Scenario 4 — Mute, stop, and mode switching (User Story 4 / SC-007, SC-008, SC-009)

1. While the AI is speaking, mute audio output. Confirm sound stops immediately (time it —
   under 200ms, SC-007) while the transcript keeps streaming and the sphere keeps reacting
   (FR-021) — i.e., generation/synthesis did not stop, only the speaker.
2. Unmute; confirm the *next* reply plays normally and the muted reply is not replayed
   retroactively (FR-022).
3. While the AI is speaking, press Stop. Confirm playback halts immediately, the sphere
   returns to idle, and (if in Continuous mode) listening resumes automatically (FR-023).
4. Switch conversation mode mid-conversation; confirm history/context is unaffected and no
   page reload occurs (FR-015 acceptance, User Story 4 acceptance scenario 4).
5. Set a distinctive voice/speed/mode/mute combination, reload the app (or log in from
   another session), and confirm every preference is restored automatically (FR-029/FR-030,
   SC-008 — verify via `GET /api/v1/ai/voice/preferences`, contracts/voice-preferences.md).
6. Repeat the microphone/mute/stop/mode controls using keyboard only, no pointer input, and
   confirm every control remains fully operable (FR-024, SC-009).

**Pass condition**: matches User Story 4's five acceptance scenarios; SC-007/SC-008/SC-009
verified as above.

## Scenario 5 — Primary provider outage triggers automatic fallback (Clarifications, FR-033–FR-037)

1. Point the backend at an unreachable/invalid ElevenLabs configuration (see Prerequisites).
2. Start a voice turn. Confirm `POST /api/v1/ai/voice/stt-session` fails
   (contracts/voice-stt-session.md) and the client automatically switches to the legacy
   Whisper/`speechSynthesis` path **for that same turn**, with a clear, visible "reduced
   quality" notice (FR-033) — the conversation is not interrupted or restarted.
3. Confirm the fallback voice still sounds like the same branded persona (FR-009's fallback
   clause) — not a generic/mismatched voice.
4. Confirm a `VoiceProviderFailoverEvent` was recorded (`Direction: FailedOverToFallback`) —
   verify via `GET /api/v1/ai/voice/health` as the admin user (contracts/voice-provider-health.md).
5. Restore a valid ElevenLabs configuration. Start a new voice turn in the *same* session
   (don't reload). Confirm the system automatically retries and switches back to the primary
   provider **before this next turn begins**, with no manual action (FR-034, SC-010).
6. Confirm a second `VoiceProviderFailoverEvent` was recorded (`Direction:
   RecoveredToPrimary`) and that `GET /api/v1/ai/voice/health`'s `currentStatus` reads
   `healthy` again (SC-011).

**Pass condition**: matches the relevant Edge Cases and FR-033/FR-034/FR-039 in spec.md;
SC-005/SC-010/SC-011 all verified in one continuous run.

## Scenario 6 — Both engines unavailable (edge case)

1. With the primary provider still unreachable (Scenario 5 step 1), also block microphone
   permission at the browser level (simulating the fallback's own capture path being
   unavailable).
2. Confirm the system shows a clear, visible error with retry/exit-voice-mode options
   (FR-036) rather than an indefinite "listening"/"processing" state (FR-032).
3. Confirm the user can still fall back to typed text input without losing the conversation
   (FR-038).

**Pass condition**: matches spec.md's "both primary and fallback unavailable" edge case and
FR-036/FR-038.
