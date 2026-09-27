# API Contract: Admin Dictation Settings

Research: D7, D9, D10, D12. These routes live on `AdminVoiceProvidersController` under
`api/v1/admin/voice`, with the same authorization policy (Administrator) and the same
`[EnableRateLimiting("admin-endpoints")]`. The UI is a new "Dictation" section on Admin → Voice,
next to the voice generator (FR-003).

## Read the setting

`GET /api/v1/admin/voice/dictation`

`200 OK`:
```json
{
  "primaryEngine": "LocalWhisper",
  "pushToTalkEngine": "LocalWhisper",
  "state": "Active",
  "suspension": null,
  "lastRevert": {
    "atUtc": "2026-09-27T09:00:00Z",
    "reason": "VendorSwitchedOff",
    "from": "OpenAiWhisper"
  },
  "engines": [
    { "engine": "LocalWhisper",       "selectable": true,  "unavailableReason": null },
    { "engine": "OpenAiWhisper",      "selectable": false, "unavailableReason": "OpenAI is switched off under AI providers." },
    { "engine": "ElevenLabsRealtime", "selectable": true,  "unavailableReason": null }
  ],
  "pushToTalkEngines": [
    { "engine": "LocalWhisper",  "selectable": true,  "unavailableReason": null },
    { "engine": "OpenAiWhisper", "selectable": false, "unavailableReason": "OpenAI is switched off under AI providers." },
    { "engine": "Browser",       "selectable": true,  "unavailableReason": null }
  ],
  "localWhisper": {
    "selectedModelId": null,
    "effectiveModel": { "label": null, "ready": false, "problem": "No Local Whisper model is selected, so dictation uses the browser built-in. Deploy ggml-base.bin under Custom Models, then select it here." },
    "models": [
      { "id": "7b1…", "label": "whisper.cpp (ggml-base.bin)", "selectable": true, "reason": null },
      { "id": "9c2…", "label": "supertonic-3", "selectable": false, "reason": "Deploy the model from a URL that names its .bin file." }
    ]
  },
  "rowVersion": "AAAAAAAAB9E="
}
```

- `suspension` while Suspended:
  `{ "engine": "OpenAiWhisper", "atUtc": "…", "reason": "Quota exhausted", "browserInUse": true }`.
  The page shows a banner stating that the browser built-in is being used wherever that engine
  would have served (FR-016).
- `effectiveModel.problem` explains why Local Whisper isn't serving: no model selected (a fresh
  deployment, FR-002), the selected deployment is Unavailable (FR-010), or its file is missing or
  unreadable (a failure, FR-007). In the first two cases the browser built-in serves as the normal
  path.
- `pushToTalkEngine` is used only while `primaryEngine` is `ElevenLabsRealtime`; the page shows it
  only then (FR-017).
- `models` lists every Completed, non-deleted Custom Models deployment with the reason it is or
  isn't selectable (FR-009a).

## Set the primary engine

`PUT /api/v1/admin/voice/dictation/primary`
```json
{ "engine": "OpenAiWhisper", "rowVersion": "AAAAAAAAB9E=" }
```
- `204` on success. This always clears a suspension, including when the same engine is set again
  after a renewal (FR-016).
- `422` (`dictation-engine-not-selectable`) when the vendor is switched off (FR-004).
- `409` on a stale `rowVersion`.

## Set the Push-to-Talk engine (FR-017)

`PUT /api/v1/admin/voice/dictation/push-to-talk`
```json
{ "engine": "LocalWhisper", "rowVersion": "AAAAAAAAB9E=" }
```
- `engine`: `LocalWhisper` / `OpenAiWhisper` / `Browser`.
- `204` on success; clears a suspension of that engine's vendor.
- `422` (`dictation-engine-not-selectable`) for `OpenAiWhisper` while OpenAI is switched off.
- `409` on a stale `rowVersion`.

## Select the Local Whisper model

`PUT /api/v1/admin/voice/dictation/local-whisper-model`
```json
{ "customModelId": "7b1…", "rowVersion": "…" }
```
`customModelId: null` = no model (Local Whisper paths use the browser built-in).
- `204` on success.
- `422` (`local-whisper-model-not-selectable`) with the reason (FR-009a).
- `409` on a stale `rowVersion`.

This does **not** change the primary engine: selecting a model and choosing Local Whisper as primary
are separate decisions.

## Try a model

`POST /api/v1/admin/voice/dictation/try` — `multipart/form-data`, `[RequestSizeLimit(4 MB)]`.

| Part | Rules |
|---|---|
| `file` | A 16 kHz mono WAV sample, recorded on the page with the same encoder as dictation. |
| `customModelId` | Required. Must be selectable. |
| `language` | Optional hint. |

`200 OK`:
```json
{ "text": "the transcript", "elapsedMs": 1840, "modelLabel": "whisper.cpp (ggml-small.bin)" }
```

- `422` when the model isn't selectable or the WAV is invalid.
- `409` (`try-in-progress`) when another try is running (one at a time, research D12).
- `503` with the reason as `detail` when the model failed to load or transcribe. Admins may see the
  reason; nothing goes on the trail and no user is affected (FR-009c).

## Custom Models removal guard

`DELETE /api/v1/admin/custom-models/{id}` (existing) now returns `409`
(`custom-model-selected-for-local-whisper`, "Select a different Local Whisper model first.") when
the deployment is the selected Local Whisper model (FR-009b).

## Vendor switch-off (FR-015)

No new route. `PATCH /api/v1/admin/ai/providers/{id}` (existing), when it switches OpenAI or
ElevenLabs off while that vendor backs the primary or the Push-to-Talk engine, reverts each such
choice to Local Whisper in the same transaction. The next `GET …/dictation` shows `lastRevert`.
Switching a vendor on never selects it.
