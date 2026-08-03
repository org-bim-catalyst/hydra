# API Contract: User Voice Preferences

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New actions on the existing `AiController`, same `[Authorize]` + `[EnableRateLimiting
("ai-endpoints")]` policy. Mirrors the existing `GET`/`PUT` shape of
`/api/v1/ai/preferences` (specs/005-multi-provider-ai-engine contracts/preferences.md) applied
to `UserVoicePreference` instead of `UserAiPreference` — deliberately a separate resource, not
merged into the existing AI-preferences payload, since voice preferences are a distinct
concern with their own entity (data-model.md) and don't share a lifecycle with chat defaults.

## Get the caller's voice preferences

`GET /api/v1/ai/voice/preferences`

Response (`200 OK`) — returns platform defaults if the caller has never saved a preference
(no row created until first save, same "created lazily" pattern as `UserAiPreference`):
```json
{
  "conversationMode": "PushToTalk",
  "isMuted": false,
  "selectedVoiceId": null,
  "voiceSpeed": null,
  "voiceStyle": null,
  "preferredMicrophoneDeviceId": null,
  "preferredSpeakerDeviceId": null
}
```

## Save the caller's voice preferences

`PUT /api/v1/ai/voice/preferences`

Request body — any subset of the fields above; omitted fields leave the existing stored value
unchanged (partial update, same semantics as spec 005's preferences endpoint).

`400` if `voiceSpeed`/`voiceStyle` fall outside ElevenLabs' allowed range (data-model.md
validation rule) — rejected with a specific message, never silently clamped.

Response: `204 No Content` on success.

## Frontend usage note

This endpoint persists the fields that must survive across devices/browsers (FR-029/FR-030 —
conversation mode, mute state, selected voice, speed/style, and *which* device id was last
selected). `PreferredMicrophoneDeviceId`/`PreferredSpeakerDeviceId` are opaque browser device
identifiers stored as an echo for convenience (FR-031's "fall back to a working default and
visibly notify" logic runs entirely client-side by comparing the stored id against
`navigator.mediaDevices.enumerateDevices()` at session start — this endpoint does not validate
device availability, since a device present on one of a user's machines may be absent on
another).
