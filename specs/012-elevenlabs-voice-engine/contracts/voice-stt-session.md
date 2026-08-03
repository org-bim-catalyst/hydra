# API Contract: Speech-to-Text Session Token

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md) |
**Research**: [../research.md](../research.md) Decisions 2, 8, 9

New action on the existing `AiController`, under its existing `[Authorize]` +
`[EnableRateLimiting("ai-endpoints")]` policy (research.md Decision 7).

## Mint a speech-to-text session token

`POST /api/v1/ai/voice/stt-session`

Request body:
```json
{
  "language": "en"
}
```
`language` is the same value `ChatPage.tsx` already tracks and passes to the legacy
`useTextToSpeech.speak(text, language)` call today (research.md Decision 9) — required, so
STT can be hinted to the caller's selected language rather than relying on autodetection.

Server behavior:
1. Calls ElevenLabs server-side (using `ElevenLabsOptions.ApiKey`, never exposed to the
   client) to obtain a single-use, short-lived realtime-STT token, passing `language` through
   as ElevenLabs' `language_code` parameter (research.md Decision 9; realtime-endpoint support
   for this parameter is a residual verification item — research.md).
2. On success, records nothing (a successful mint is the "healthy" case — only failures and
   recoveries are logged, per data-model.md's `VoiceProviderFailoverEvent`).
3. On failure (ElevenLabs unreachable, rate-limited, or errors), records a
   `VoiceProviderFailoverEvent` (`Direction = FailedOverToFallback`, `Reason` = a short
   sanitized summary) and returns an error — the client is expected to fall back to the
   legacy Whisper-based capture path (`/api/v1/ai/transcriptions/microphone`, unchanged) for
   this turn (FR-033).

**Transient vs. failover distinction (research.md Decision 8)**: this endpoint itself has no
retry logic server-side — it is the *client* (`useSpeechRecognition.ts`) that retries calling
this endpoint (and reconnecting the resulting WebSocket) up to 2 times, 1 second apart, before
treating a failure here as "primary is down" and invoking the fallback path below. A failure
on this endpoint is not, by itself, proof of an outage — only exhausted client-side retries
are.

Response (`200 OK`):
```json
{
  "token": "opaque short-lived token",
  "expiresAtUtc": "2026-08-02T10:15:00Z"
}
```

Response (failure, RFC 7807 Problem Details, same shape/error-type vocabulary already
established by spec 005's `ai-provider-unavailable`/`ai-provider-rate-limited` types —
reused here rather than inventing voice-specific error types):
```json
{
  "type": "ai-provider-unavailable",
  "title": "Voice provider unavailable",
  "status": 503,
  "detail": "The primary voice provider could not be reached.",
  "traceId": "..."
}
```

## Client usage (frontend contract, not a new server behavior)

The browser passes `token` directly to a WebSocket connection opened **to ElevenLabs**, not to
this backend (research.md Decision 2) — this endpoint's only job is minting the token. The
token expires in ~15 minutes (ElevenLabs-documented); a session running longer than that must
call this endpoint again before the token lapses. The client retries this endpoint and the
resulting WebSocket connection up to 2 times, 1 second apart (research.md Decision 8) before
treating either of the following as "primary path failed for this turn, use fallback" (FR-033):
- This endpoint returning a non-2xx response after the retry budget is exhausted.
- The resulting WebSocket failing to connect, or closing unexpectedly before a transcript is
  produced, after the retry budget is exhausted.

A single transient failure that succeeds on retry (FR-004) is invisible to the user and does
**not** call the fallback path or record a `VoiceProviderFailoverEvent`.

Before each subsequent voice turn while the session is on the fallback engine, the client
calls this endpoint again as a health probe (research.md Decision 5); a `200` response means
the primary has recovered and the session switches back before that turn begins (FR-034).
