# Quickstart: Validating SPEC-034

## Prerequisites

- Backend running locally; frontend dev server against it.
- Browser with microphone permission granted; device speakers for Continuous-mode scenarios.

## Scenario 1 — Upload guard & logging (User Story 1, P1)

1. (Automated) Send a `POST /api/v1/ai/transcriptions` request with no `file` part (or an empty
   file) — confirm a 400 with a specific title, not a 500.
2. Record a normal, well-formed voice message — confirm it still transcribes successfully
   (regression check against specs/032/033).
3. (Process) Confirm `Serilog:WriteTo` in `appsettings.Production.json` now includes a file sink,
   and that a deliberately-triggered server-side exception (e.g., via the new guard's sibling
   paths, or a temporary local test) produces a retrievable log file entry.

## Scenario 2 — Dual tap/hold gesture (User Story 2, P1)

1. In the Expanded panel, click the mic once (a tap, no hold).
   - **Expected**: recording starts; a waveform and both a checkmark and a cancel control appear;
     nothing is sent yet.
2. Tap the checkmark.
   - **Expected**: recording stops, transcribes, and the result appears in the message field.
3. Repeat step 1, then tap the cancel (✗) control instead.
   - **Expected**: recording stops immediately; nothing is transcribed or sent; the message field
     is unchanged.
4. Press and hold the mic, speak, and release.
   - **Expected**: only the waveform is shown throughout the hold — no checkmark/cancel appear at
     any point; releasing transcribes and populates the message field automatically, with no
     further tap.

## Scenario 3 — Dedicated Continuous voice view (User Story 3, P1)

1. From the normal chat view, click the mode-switch button to activate Continuous mode.
   - **Expected**: the interface transitions to a focused voice view showing Lucy's reactive
     presence and exactly two controls: Exit and Mute. The text composer is not visible.
2. Speak; let Lucy respond.
   - **Expected**: the conversation proceeds normally within this view; her own voice is not
     picked up as user speech (specs/033's mute mechanism, now actually wired to this view).
3. Tap Mute while she's speaking.
   - **Expected**: her audio silences immediately; the view stays open; the control reflects the
     muted state.
4. Tap Exit.
   - **Expected**: the live session stops; the interface returns to the normal chat view; the
     conversation that happened in the voice view appears in the message history.
5. Reload the chat (with Continuous still the saved preference).
   - **Expected**: the normal chat view loads — the dedicated voice view does NOT open
     automatically; no microphone permission prompt fires on load.

## Success criteria mapping

| Scenario | Validates |
|---|---|
| 1 | SC-001, SC-002, SC-003 |
| 2 | SC-004 |
| 3 | SC-005, SC-006 |
