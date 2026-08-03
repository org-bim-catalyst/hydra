# API Contract: Voice Reply Stream (text + synthesized speech, multiplexed)

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md) |
**Research**: [../research.md](../research.md) Decisions 3, 9

New action on the existing `AiController`, same `[Authorize]` +
`[EnableRateLimiting("ai-endpoints")]` policy as `/api/v1/ai/chat`. Used only for voice-mode
turns on the primary (ElevenLabs) path; typed chat and fallback-mode voice turns keep using
the existing, unmodified `/api/v1/ai/chat`.

## Send a voice turn and stream back text + speech together

`POST /api/v1/ai/voice/reply`

Request body — the existing `ChatRequest` used by `/api/v1/ai/chat` (`chatId`, `messages`
ending in the user's finalized transcript, `providerId`, `modelId`, `generationParameters`),
plus one addition:
- `language` (string, required) — the same value passed to `POST /api/v1/ai/voice/stt-session`
  (contracts/voice-stt-session.md) and already tracked by `ChatPage.tsx` today (research.md
  Decision 9). Used to select the per-language ElevenLabs voice id from `ElevenLabsOptions`
  when the caller has no `UserVoicePreference.SelectedVoiceId` override.

The same LLM-selection/generation-parameter validation rules from
specs/005-multi-provider-ai-engine apply unchanged. Voice id/model/speed/style/speaker-boost
are otherwise resolved server-side from the caller's `UserVoicePreference` (falling back to
`ElevenLabsOptions`' per-language default, then its platform-wide default), consistent with
how generation-parameter defaults already cascade in `/api/v1/ai/chat`.

Server behavior, per the existing `AppendMessageCommand` composition already used by
`AiController.Chat`:
1. Persists the user's message exactly as `/api/v1/ai/chat` does today.
2. Opens the response as a chunked-transfer stream (`Content-Type: text/event-stream`,
   `Cache-Control: no-cache`) — same headers `/api/v1/ai/chat` already sets.
3. Invokes the existing `SendChatMessageCommand`/`IAIProviderResolver` stream, buffering the
   growing text into sentence/clause-sized chunks as it arrives.
4. Feeds each ready chunk to the ElevenLabs TTS streaming call, relaying the resulting audio
   bytes to the client as they arrive — interleaved with the underlying text deltas.
5. Persists the assistant's full message once the LLM stream completes, exactly as
   `/api/v1/ai/chat` does today (same `Message` row shape, same usage/cost fields from spec
   005 — this endpoint adds no new persisted fields, per data-model.md's "Modified Entities:
   None").
6. If the ElevenLabs TTS call fails mid-stream (but the LLM stream is still healthy), emits an
   `audio-failed` event and records a `VoiceProviderFailoverEvent`
   (`Direction = FailedOverToFallback`) — the client falls back to speaking the already-
   received text via the legacy `speechSynthesis` path for the remainder of that turn (FR-033)
   while the text stream itself continues uninterrupted (a TTS failure never truncates or
   fails the underlying chat response).

## Response framing

Same `data: {...}\n\n` framing `/api/v1/ai/chat` already uses, with a JSON envelope carrying a
`type` discriminator so one stream can carry both text and audio (research.md Decision 3 —
a structured evolution of the existing convention, not a new protocol):

```
data: {"type":"transcript-delta","content":"Sure, here's"}

data: {"type":"transcript-delta","content":" what I found..."}

data: {"type":"audio-chunk","sequence":0,"audio":"<base64 mp3/pcm bytes>"}

data: {"type":"audio-chunk","sequence":1,"audio":"<base64 mp3/pcm bytes>"}

data: {"type":"provider-status","voiceProvider":"primary"}

data: {"type":"usage","inputTokens":142,"outputTokens":58,"latencyMs":410}

data: {"type":"done"}

```

Event types:
- `transcript-delta` — identical meaning to `/api/v1/ai/chat`'s raw text chunks, just wrapped
  in an envelope; drives the on-screen transcript exactly like typed chat does.
- `audio-chunk` — base64-encoded audio bytes in arrival order (`sequence` lets the client
  detect gaps); fed into the shared `AudioContext`/`AnalyserNode` (`useVoiceAnalyzer.ts`) for
  both playback and sphere reactivity, per FR-025/FR-026 (playback and visualization begin as
  soon as the *first* `audio-chunk` arrives, not once the stream completes).
- `provider-status` — emitted once at stream start (and again if a mid-stream `audio-failed`
  fallback occurs) so the client's Voice Provider Status stays in sync with which engine
  actually produced (or is producing) the audio for this turn.
- `audio-failed` — TTS-specific failure mid-stream (see server behavior step 6 above); the
  text stream (`transcript-delta`/`done`) continues regardless.
- `usage` — same fields `/api/v1/ai/chat` already reports, unchanged.
- `error` — same RFC 7807-style Problem Details error types as `/api/v1/ai/chat`
  (`ai-provider-unavailable`, `ai-provider-authentication-failed`, `ai-provider-rate-limited`)
  when the *LLM* call itself fails — this endpoint does not invent new LLM-error types on top
  of what spec 005 already established.
- `done` — closes the stream, same meaning as `/api/v1/ai/chat`'s `data: [DONE]\n\n`.

## Cancellation (interruption / stop)

Client-initiated cancellation (`AbortController`, same pattern as the existing
`useChatStream.ts`) propagates via the request's `CancellationToken`, exactly as
`SendChatMessageCommandHandler` → `IAIProvider.StreamChatAsync` already does for
`/api/v1/ai/chat` — cancelling this request stops **both** the underlying LLM generation and
any in-flight TTS synthesis together, satisfying FR-017 ("immediately stop AI audio playback
and any further generation/synthesis of that reply") with the same mechanism already proven
for text cancellation, no new cancellation plumbing required.

**Duck vs. cancel (research.md Decision 10)**: the client may locally mute/duck the audio
already received (via `useVoiceAnalyzer`'s gain node) *before* deciding to actually call
`AbortController.abort()` on this request — a fast, reversible, purely client-side reaction to
a locally-detected possible interruption, used to hit SC-002's latency target without waiting
for its own STT session to confirm real speech. Only once that confirmation arrives does the
client abort this request; if the local trigger turns out to be a false positive, the client
simply un-ducks and playback continues from where it paused, with no request-level effect on
this endpoint at all.
