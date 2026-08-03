# Phase 1 Data Model: ElevenLabs Conversational Voice Engine

New entities live in `AskLucy.Domain/Ai/`, configured in `AskLucy.Persistence/Configurations/`,
per constitution §3 (Domain purity — no EF Core attributes on Domain types; all mapping via
Fluent API). Surrogate keys are `Guid` v7 (`Guid.CreateVersion7()`), matching every existing
entity. Audit columns (`CreatedAtUtc`/`CreatedBy`/`ModifiedAtUtc`/`ModifiedBy`/`DeletedAtUtc`/
`DeletedBy`) come from `BaseEntity` + `AuditSaveChangesInterceptor`, exactly as on every
existing entity, and are not repeated per-entity below except where an entity deliberately
omits soft delete.

## New Entities

### UserVoicePreference

A user's persisted voice settings (FR-029/FR-030/SC-008). Mirrors `UserAiPreference`'s
"created lazily on first save" shape.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserId` | `string` | Required, unique (one row per user, like `UserAiPreference`). |
| `ConversationMode` | `enum` (`PushToTalk`, `Continuous`) | FR-016. Defaults `PushToTalk` on first creation (matches today's only mode). |
| `IsMuted` | `bool` | FR-029. Defaults `false`. |
| `SelectedVoiceId` | `string?` (max 100) | ElevenLabs voice id (FR-010). Null = platform default persona voice. |
| `VoiceSpeed` | `float?` | FR-010. Validated in `SaveUserVoicePreferenceCommandValidator` against ElevenLabs' allowed range (Application-layer concern, not a Domain invariant — same pattern as spec 005's generation-parameter validation). |
| `VoiceStyle` | `float?` | FR-010, same validation approach. |
| `PreferredMicrophoneDeviceId` | `string?` (max 200) | FR-029. Browser `MediaDeviceInfo.deviceId` — opaque string, no server-side meaning beyond storage/echo-back. |
| `PreferredSpeakerDeviceId` | `string?` (max 200) | FR-030, same shape. |

**Validation rules**: `UserId` required, unique. `VoiceSpeed`/`VoiceStyle`, when present, must
fall within ElevenLabs' documented allowed range (confirmed at implementation time per
research.md's residual verification risk #1) — rejected with a specific 400 otherwise, never
silently clamped (constitution §2.VIII, no-silent-failures).

**Relationships**: None (unlike `UserAiPreference`, there's no FK to a provider/model catalog
row — ElevenLabs voice selection is a plain string id, not a foreign key into an admin-curated
catalog, per research.md Decision 4).

**State transitions**: None beyond simple field updates via `SaveUserVoicePreference`.

**Lifecycle note — FR-031**: "previously saved preference no longer available" (e.g., a
disconnected microphone) is **not** modeled as a state transition on this entity — it's
detected client-side at the moment a saved device id is compared against the browser's current
`navigator.mediaDevices.enumerateDevices()` list, and handled entirely in the frontend Voice
State Machine (falls back to the current default device, shows a notice). The stored
`PreferredMicrophoneDeviceId` value itself is left unchanged so the device can be picked back
up automatically if reconnected later.

---

### VoiceProviderFailoverEvent

Append-only history of voice-session failovers between the primary (ElevenLabs) and legacy
fallback engines (FR-033/FR-034/FR-039, SC-011). Mirrors `ProviderHealthCheck`'s shape and its
documented "log, not user-editable data" exception to soft delete.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserId` | `string` | Required, indexed together with `OccurredAtUtc` — lets an admin distinguish one user's flaky network from a platform-wide outage (research.md Decision 5). |
| `OccurredAtUtc` | `DateTime` | Required, indexed — admin views query "recent events" and "events per hour" (FR-039). |
| `Direction` | `enum` (`FailedOverToFallback`, `RecoveredToPrimary`) | Required. |
| `Reason` | `string?` (max 500) | A short, sanitized error summary (e.g., "stt-session request timed out", "reply stream returned 503") — never the raw provider exception verbatim if it could contain the API key or other secret (constitution §14, same rule already documented on `ProviderHealthCheck.Detail`). |

**Validation rules**: None beyond required fields — this is a log, not a user-editable entity
(same as `ProviderHealthCheck`).

**Relationships**: None — deliberately not FK'd to `UserChat`/`Message`; a failover event is
about *provider health*, not about any one conversation's content, and must never carry
transcript/response text (FR-041 — never retain more than needed, and never log content
alongside PII-adjacent operational data).

**Lifecycle note**: No soft delete (`DeletedAtUtc`) — append-only operational log, same
documented exception as `ProviderHealthCheck`. A retention/pruning job is out of scope for
this spec (no FR requires it), flagged as a future operational concern only.

---

## Explicitly NOT New Entities

The spec's Key Entities list several concepts that are **not** implemented as new database
tables — recorded here so a future reader doesn't wonder why they're "missing":

- **Voice Session / Voice Turn / Voice State**: these are runtime, per-tab concepts (which
  mode is active, what the state machine's current state is, whether the current turn was
  interrupted) with no independent query or reporting need of their own — every voice turn's
  *durable content* (the transcript and the AI's reply) already persists through the existing
  `AppendMessageCommand`/`Message`/`UserChat` tables exactly as a typed chat turn does today
  (spec Assumption: "underlying conversation/session infrastructure is reused"). Modeling
  them as new tables would duplicate data with no independent lifecycle — constitution §III
  (DRY/simplicity) rejects that. They are implemented as TypeScript types plus a Zustand store
  (`useVoiceState.ts`, `useConversationAudio.ts`) on the frontend only.
- **Voice Persona**: not user or session data — it's configuration (`ElevenLabsOptions
  .VoiceId`/`.ModelId`/default voice settings for the primary path, the existing
  `voicePersonaMap.ts` for the fallback path). No table needed.
- **Voice Provider Status**: the spec's Key Entity describing "which implementation is active
  right now, and whether recovery is pending" is transient per-session frontend state
  (`voiceProviderStatus.ts`), not a persisted row — `VoiceProviderFailoverEvent` above is its
  durable, append-only *history*, which is the only part that needs to survive past the
  session and be queryable by admins.

## Modified Entities

**None.** `Message` and `UserChat` are unchanged. A voice turn's transcript is submitted
through the same request shape `/api/v1/ai/voice/reply` accepts (see
contracts/voice-reply-stream.md) and persisted via the same `AppendMessageCommand` composition
`AiController.Chat` already uses for typed messages — no new column, no new modality marker.

A `Message.InputModality`/`OutputModality` field (e.g., "voice" vs. "text", for future
analytics) was considered and rejected for now: no functional requirement in spec.md asks for
that distinction to be surfaced anywhere, so adding it would be speculative schema per
constitution §III (YAGNI). If a future spec needs to report on voice vs. text usage, that's a
small, additive migration at that time — not a reason to add an unused column now.
