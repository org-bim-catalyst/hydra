# Data Model: Restore Voice Output Mute & Input Mode Controls

This feature introduces **no new persisted entities and no schema changes**. It reuses
the `UserVoicePreference` entity and `/api/v1/ai/voice/preferences` contract delivered by
spec `012-elevenlabs-voice-engine` unchanged (see
[../012-elevenlabs-voice-engine/data-model.md](../012-elevenlabs-voice-engine/data-model.md)
for the full original data model). This document covers only the two entities named in
spec.md's Key Entities section and the client-side runtime state this feature's UI wiring
depends on.

## Persisted Entities (reused, unchanged)

### Voice Output Preference → `UserVoicePreference.IsMuted`

| Field | Type | Notes |
|---|---|---|
| `IsMuted` | `bool` | Persisted server-side per user via `PUT /api/v1/ai/voice/preferences` (`SaveUserVoicePreferenceCommand`), cached client-side in `voicePreferencesStore` (Zustand + localStorage), and hydrated on load via `GET /api/v1/ai/voice/preferences`. |

**Validation rules**: None beyond type — a boolean has no invalid state. Defaults to
`false` (unmuted) for a user with no saved preference (`UserVoicePreference.Create`).

### Voice Input Mode Preference → `UserVoicePreference.ConversationMode`

| Field | Type | Notes |
|---|---|---|
| `ConversationMode` | `VoiceConversationMode` enum (`PushToTalk` \| `Continuous`) | Same persistence path as `IsMuted`. Defaults to `PushToTalk`. |

**Validation rules**: Closed enum — only the two named values are valid; enforced by the
C# enum type and the existing `SaveUserVoicePreferenceCommandValidator`.

**State transitions**: None at the domain-entity level — `SetConversationMode` is a pure
setter (FR-007's "no restart" behavior is a frontend orchestration concern, not a
persistence-layer state machine; see the Client-Side Runtime State section below).

## Client-Side Runtime State (not persisted — exists only for the life of a browser tab)

This feature does **not** use `useVoiceState` or `useConversationAudio` (research.md
Decision 1) — both remain unused/unchanged, available for a future full conversational-mode
feature. Runtime state instead comes directly from the two hooks already involved:

### Recognition state (`useSpeechRecognition`, existing, reused unchanged)

`isListening: boolean`, `permissionState: 'unknown' | 'granted' | 'denied'`, `error: string
| null` — all pre-existing return values. `ChatComposer`'s new mic control and
`VoiceControlBar` derive their state directly from these.

### Output state (`useVoiceOutput`, existing, extended)

`isSpeaking: boolean` (pre-existing) plus new `isMuted: boolean` (Decision 3, research.md).

**Derived states this feature adds logic around** (not new store/hook fields — computed in
the component layer):
- **Push-to-talk capture in progress** := `conversationMode === 'PushToTalk' &&
  recognition.isListening`. Used to disable the mode-switch control (Decision 6,
  research.md).
- **Activation source** (hold vs. toggle) is transient interaction state local to the mic
  control (e.g., "is the pointer currently down") — not modeled in any hook/store, since it
  doesn't affect any other consumer.

### Voice Preferences Store (`voicePreferencesStore`, existing, reused unchanged)

Already holds exactly `conversationMode`, `isMuted` (plus voice/device selection fields
out of this feature's scope), synced with the server via `hydrateFromServer()`/`update()`.
This feature's UI calls `update({ isMuted })` and `update({ conversationMode })` — no new
fields, no new store.

## Relationships

```text
UserVoicePreference (1) ── per authenticated user ── (1) voicePreferencesStore (client cache)
                                                              │
                                                ┌─────────────┴─────────────┐
                                                ▼                           ▼
                                      useVoiceOutput (isMuted)   ChatComposer mic control
                                      gates speak()/stop()       (useSpeechRecognition, reads
                                      (Decision 3)                conversationMode for hold/
                                                                  toggle/continuous behavior,
                                                                  Decision 4/5)
                                                              │
                                                              ▼
                                                     VoiceControlBar (display + controls,
                                                     Decision 2 — reads both hooks' state,
                                                     calls voicePreferencesStore.update())
```

No new relationships are introduced between entities; this feature only adds new
*consumers* (UI wiring) of the existing preference data.
