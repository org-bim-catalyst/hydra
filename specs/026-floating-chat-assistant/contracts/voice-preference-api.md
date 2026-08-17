# API Contract: Voice Preference — `defaultLanguage` addition

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Extends the existing `/api/v1/ai/voice/preferences` resource (`AiController.cs`, spec 012's `contracts/voice-preferences.md`) with one new field — `defaultLanguage` — rather than introducing a new endpoint or resource (research.md #4). Same `[Authorize]` policy, same request/response shape otherwise, unchanged.

## Get the caller's voice preferences

`GET /api/v1/ai/voice/preferences`

Response (`200 OK`) — adds `defaultLanguage` alongside the existing fields; `null` until the caller has explicitly saved one (Edge Cases: "a user has never set a default language" — the frontend falls back to the assistant's current default when `null`, per FR-016/`ActiveLanguageFlag`'s contract):

```json
{
  "conversationMode": "PushToTalk",
  "isMuted": false,
  "selectedVoiceId": null,
  "voiceSpeed": null,
  "voiceStyle": null,
  "preferredMicrophoneDeviceId": null,
  "preferredSpeakerDeviceId": null,
  "defaultLanguage": null
}
```

## Save the caller's voice preferences

`PUT /api/v1/ai/voice/preferences`

Request body — any subset of the fields above; omitted fields leave the existing stored value unchanged (existing partial-update semantics, unchanged by this feature).

- `400` (Problem Details) if `defaultLanguage` is present but not one of the product's currently supported codes (`en`, `ar`, `es`, `fr`, `de` — `SaveUserVoicePreferenceCommandValidator`, data-model.md validation rule) — rejected with a specific message, never silently ignored or coerced.
- Response: `200 OK` with the full updated `UserVoicePreferenceDto` (matches this endpoint's existing, current behavior — `AiController.SaveVoicePreferences` returns `Ok(dto)`, which `useVoicePreferencesStore.update` already consumes via `set(saved)`).

## Frontend usage note

`useVoicePreferencesStore` (already server-synced + localStorage-cached) gains `defaultLanguage` as a field alongside its existing ones — no new store, no new hook. `ConversationView` reads it once on mount to seed its local `language` state (data-model.md, "Client-side: `ChatAssistantWidgetState`"); Chat Configuration's new "Default language" control (FR-017) calls the existing `update({ defaultLanguage })` path, which already has explicit save-failure error surfacing (constitution §2.VIII) built in.

## Everything else (unchanged)

Every other field on this resource, and every other `/api/v1/ai/*` route, is unmodified by this feature.
