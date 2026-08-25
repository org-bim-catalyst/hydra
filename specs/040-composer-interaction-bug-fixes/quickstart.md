# Quickstart: Composer Interaction Bug Fixes

Manual validation scenarios per user story, mapped to spec.md's acceptance scenarios and the
reference mockups (`docs/images/figure-image-{1,2,3,4,9,11}.png`). Run against the integrated app
(`dotnet run` from `src/AskLucy.Web` — serves the built ClientApp, no separate Vite server needed)
signed in with a test account.

## Prerequisites

- A signed-in session with at least one AI provider/model enabled (chat/transcription requires one).
- Microphone access available in the browser for US2/US3/US4/US5.

## US1 — Empty-state button positions

1. Open a conversation with an empty composer (no text typed).
2. Verify the attachment icon is flush against the left edge of the control row.
3. Verify the mic and continuous-conversation icons are flush against the right edge, with visible
   empty space between them and the attachment icon — compare against `figure-image-1.png`.
4. Resize the browser narrower; verify the left/right anchoring holds at every width down to the
   panel's minimum supported width.

## US2 — Typing-state keeps attach + mic

1. Type any character into the composer.
2. Verify the attachment and mic icons remain visible (compare against `figure-image-2.png`) and
   Send appears at the right, replacing only the continuous-conversation icon.
3. Tap the mic, speak briefly, release quickly; verify the transcript is appended *after* the text
   already typed, not replacing it.
4. Hold the mic instead of tapping; verify hold-to-talk still works identically to the empty state.
5. Clear all typed text; verify the composer reverts to the US1 empty-state layout.

## US3 — Recording/tap-review order

1. From the empty state, tap the mic and release quickly (under ~350ms) to enter tap-review.
2. Verify left-to-right order is: cancel (X), waveform, finish (check) — compare against
   `figure-image-3.png`.
3. Click cancel; verify the recording is discarded and the composer returns to empty.
4. Repeat and click finish instead; verify the recording is transcribed and appended to the
   composer text.

## US4 — Continuous-mode waveform + right-anchored controls

1. From the empty state, click the continuous-conversation icon.
2. Once idle-listening is reached, verify a live waveform is visible and fills the leading portion
   of the row, with mute and exit anchored to the right — compare against `figure-image-4.png`.
3. Speak; verify the waveform visibly reacts.
4. Click mute/unmute and exit; verify existing behavior (unchanged) still works from the new
   position.

## US5 — Continuous mode starts listening reliably

1. Start a **brand-new** session (no existing chat selected/created yet).
2. From the empty composer, click the continuous-conversation icon immediately.
3. Verify listening begins on its own — no need to type and send a message first. A visible
   listening indication (waveform reacting, presence sphere state) should appear without further
   action.
4. Speak; verify the assistant responds, exactly as it does when continuous mode is entered from
   an existing chat.

## US6 — Transcription error classification

Backend-focused; validate via the extended test suites rather than reproducing a live provider
outage:

1. `tests/AskLucy.Infrastructure.Tests/Ai/OpenAIProviderTests.cs` — a test asserting
   `CreateClient()` (exercised indirectly via any public method, e.g. `TranscribeAudioAsync`)
   throws `AiProviderAuthenticationException` when `OpenAIOptions.ApiKey` is null/empty.
2. `tests/AskLucy.Web.Tests/Middleware/ProblemDetailsMiddlewareTests.cs` — a test asserting a bare
   `HttpRequestException` maps to 502 `ai-provider-unavailable`, matching the existing
   `AiProviderUnavailableException` case's response shape.
3. Optional live check in a lower environment: temporarily blank the configured OpenAI API key,
   attempt a transcription, and confirm the user sees "The AI provider rejected the configured
   credential" (or equivalent existing copy) instead of "An unexpected error occurred."

## US7 — Bottom-positioned tooltips

1. Hover/focus every button in the composer's control row (all four states) — verify each tooltip
   appears below its button.
2. Trigger a tap-review recording; hover the cancel/finish controls — verify tooltips appear below.
3. Open the Collapsed chat widget's voice controls; hover each icon — verify tooltips appear below.
