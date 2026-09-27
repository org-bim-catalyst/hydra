# API Contract: Dictation Session (the health pre-check)

**Supersedes** the response shape in
[specs/012 contracts/voice-stt-session.md](../../012-elevenlabs-voice-engine/contracts/voice-stt-session.md)
(which gets a pointer to this file). Research: D6, D8, D14.

`POST /api/v1/ai/voice/stt-session` — same route, `[Authorize]`, `[EnableRateLimiting("ai-endpoints")]`.

Both capture paths call this **before opening the microphone** (FR-005a). Push-to-Talk is new to
this; Continuous mode already calls it.

## Request

```json
{ "language": "en", "mode": "PushToTalk" }
```

| Field | Rules |
|---|---|
| `language` | Required; same regex as today. |
| `mode` | `"Continuous"` or `"PushToTalk"`. **Optional, default `"Continuous"`**, so a tab loaded before the deploy keeps working. |

## Responses

`200 OK`, one of:

```json
{ "engine": "Realtime", "token": "…", "expiresAtUtc": "2026-09-27T10:15:00Z", "degraded": false }
```
The primary is ElevenLabs realtime, the mode is Continuous, and the mint succeeded. Stream as today.

```json
{ "engine": "Clip", "token": null, "expiresAtUtc": null, "degraded": false }
```
Record the utterance, convert it to 16 kHz mono WAV and post it to
[`/api/v1/ai/voice/transcriptions`](dictation-transcription.md). The server routes it to Local
Whisper or OpenAI Whisper: the primary engine, or — for Push-to-Talk while ElevenLabs realtime is
primary — the Push-to-Talk engine (FR-017). No notice is shown: this is the normal path.

```json
{ "engine": "Browser", "token": null, "expiresAtUtc": null, "degraded": false }
```
Dictate this attempt through the browser built-in (`SpeechRecognition`) from the start. No vendor
call is made. Returned in two kinds of case, told apart by `degraded`:

| Case | `degraded` | Trail |
|---|---|---|
| Nothing configured for this turn: no Local Whisper model selected (a fresh deployment, FR-002), the selected model marked Unavailable (FR-010), or the Push-to-Talk engine is `Browser` | `false` — no notice | nothing |
| The engine is Suspended (FR-016) | `true` | not re-reported on every call |
| The engine's local health check fails: the selected Local Whisper model's file is missing or unreadable; OpenAI switched off or credential unresolvable | `true` | failover, `fallbackServed: true` |

The value `"Whisper"` is **no longer returned**. It is renamed `"Clip"`, because a clip may be
transcribed by Local Whisper or OpenAI Whisper. `degraded` is new; a client that ignores it
behaves as today.

### Failure

As today: a transient ElevenLabs mint failure (Continuous, ElevenLabs realtime primary) returns
`503` Problem Details (`ai-provider-unavailable` / `ai-provider-rate-limited`). It is recorded as a
failover, and the client keeps its 2-retry-then-fall-back behavior; the fallback is now the
**browser built-in** only, never a clip engine.

A mint failure of a Critical kind (research D8) instead **suspends** the setting and returns
`200 { "engine": "Browser", "degraded": true }`, so the client does not retry a vendor that will
keep refusing.

The response never carries vendor or internal detail (FR-007).
