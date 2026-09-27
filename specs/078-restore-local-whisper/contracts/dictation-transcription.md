# API Contract: Dictation Clip Transcription

Research: D3, D4, D5, D8, D13, D14.

`POST /api/v1/ai/voice/transcriptions` — new action on `AiController`, `[Authorize]`,
`[EnableRateLimiting("ai-endpoints")]` (partitioned by `RateLimitPartitions.UserOrClientKey`),
`[RequestSizeLimit(4 MB)]`.

The existing `POST /api/v1/ai/transcriptions` (file-attach upload, OpenAI Whisper, any audio
format) is **unchanged** and is not used for dictation any more.

## Request

`multipart/form-data`:

| Part | Rules |
|---|---|
| `file` | Required, non-empty, ≤ 4 MB. Must be a RIFF/WAVE file with PCM format 1, 1 channel, 16000 Hz, 16 bits per sample (FR-013). The server reads the header; the declared content type is not trusted. |
| `language` | Optional; the same regex as `/ai/transcriptions` (`^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,4})?$`). Local Whisper uses it as the language hint and auto-detects when it is absent. |

## Server behavior

1. Read the setting (data-model.md) and resolve the clip engine: the primary, or the Push-to-Talk
   engine while ElevenLabs realtime is primary (FR-017). If it is the browser built-in, no Local
   Whisper model is selected, the selected model is Unavailable, or its vendor is Suspended →
   `503` (the client should not have recorded; the stt-session answered `Browser`). Nothing is
   recorded, because nothing failed or the suspension was already reported.
2. Validate the WAV header. Invalid → `422`, reported with `ReportFailure` (kind
   `ValidationFailed`).
3. Pick the `IDictationClipTranscriber` for that clip engine (research D4). No other transcriber
   is ever tried (FR-005).
4. Success → `ReportServed` → `200`.
5. Failure → `ReportFailover(..., fallbackServed: true)` (the client retries through the browser
   built-in). If the classified kind is Critical and the engine is a cloud engine → `Suspend` and
   commit (FR-016). Then `503`.

## Responses

`200 OK`:
```json
{ "text": "the transcript", "language": "en" }
```
`language` is the detected language when the engine reports one, otherwise the hint or `null`.

`422` Problem Details, `type: "dictation-audio-invalid"`, generic `detail`.

`503` Problem Details, `type: "dictation-engine-unavailable"`, `title: "Dictation unavailable"`,
generic `detail`, with `traceId` (the correlation id on the trail).

The frontend treats **any** non-200 (including a network error) the same way: gentle repeat, then
the next attempt through the browser built-in (FR-005b).
