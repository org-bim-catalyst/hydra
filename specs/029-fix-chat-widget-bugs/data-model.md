# Phase 1 Data Model: Chat Widget Reliability & Voice UI Consolidation

This feature introduces no new persisted entities and no schema changes beyond applying
an already-authored migration (research.md Decision 1). What follows are the conceptual
entities/state machines this feature reads, reshapes the *presentation* of, or adds a
health signal for — not new database tables.

## Voice Preference (existing — `AskLucy.Domain.Ai.UserVoicePreference`)

Unchanged shape. Relevant fields for this feature:

| Field | Type | Notes |
|---|---|---|
| `ConversationMode` | `'PushToTalk' \| 'Continuous'` | Drives which mic interaction model the consolidated control presents (Decision 5). |
| `IsMuted` | `bool` | Speaker-output (TTS) mute — unrelated to the microphone; preserved as its own control (FR-006a). |
| `DefaultLanguage` | `string?` | The column added by the migration this feature applies; its absence on the deployed table is the root cause of Bug 1. |

**Fallback defaults** (`voicePreferencesStore.ts` `DEFAULTS`, unchanged by this
feature): used whenever the server fetch fails, per FR-002. Post-fix, the fetch is a
TanStack Query `useQuery` (Decision 4); on error, the query's own error state drives a
small local UI indicator (Decision 3) instead of a store-level `error` string driving a
blocking Snackbar.

## Recording Control (frontend UI state — no backend representation)

The single consolidated mic control's rendered state is the product of two independent
axes, both already defined in the codebase:

- **Mode** (`ConversationMode`, `useSpeechRecognition.ts:12`): `'push-to-talk' |
  'continuous'` (note: this internal hook-level type is lowercase-hyphenated; the
  server/store-facing type is `'PushToTalk' | 'Continuous'` — pre-existing, unrelated to
  this feature, not something this feature needs to unify).
- **Phase** (`RecordingPhase`, `useVoiceRecorder.ts:5`): `'idle' | 'recording' |
  'reviewing' | 'transcribing'` — only meaningful in Push-to-Talk mode; stays `'idle'`
  throughout Continuous mode (per the existing contract note in
  `chat-widget-components.md:90`, unchanged).

| Mode | Phase | Consolidated control renders |
|---|---|---|
| Continuous | `idle` | Mic icon = microphone mute toggle (on/off), reflecting `recognition.isListening`. No "Listening…" text (FR-014) — the icon's own pulse while active is sufficient. |
| Push-to-Talk | `idle` | Mic icon = press/hold-to-record trigger. |
| Push-to-Talk | `recording` | Mic icon area replaced by waveform + Cancel (X). No "Listening…" text here either. |
| Push-to-Talk | `reviewing` | Waveform frozen + Cancel (X) / Confirm (✓) (`RecordingReviewControls`, unchanged). |
| Push-to-Talk | `transcribing` | Same as `reviewing`, controls disabled pending transcription result. |

One further control sits beside the mic, independent of mode/phase (Decisions 5/5a): a
single speaker-mute icon (`isMuted`/`onToggleMute`, FR-006a/b) that merges the former
separate "mute future replies" and "stop the current reply" actions — pressing it always
mutes (silencing an in-progress reply immediately, if any) and stays muted until pressed
again, independent of the microphone. No "Lucy is speaking…" indicator is added inside
the chat panel (FR-013): that state is already shown by `AiPresenceCard` (the reactive
presence "sphere"), a persistent, always-rendered indicator elsewhere on the workspace,
driven by the same TTS intensity signal, unaffected by this feature. The Collapsed
widget's `VoiceAnalyzer` waveform is a separate, unrelated concern — it visualizes the
*user's* microphone activity, not Lucy's.

## Real-time Connection (SignalR hubs — no new entity)

Six existing hub types, unchanged server-side logic, only their reachability is fixed
(Decision 7):

`DocumentProcessingHub`, `RetrievalIndexingHub`, `MemoryHub`, `AgentExecutionHub`,
`WorkflowExecutionHub`, `PanelHub` — each mapped at a `/hubs/<name>` path
(`Program.cs:589-594`, unchanged paths/mappings). This feature changes *only* whether
GET requests to those paths reach the hub (routing precedence, Decision 7), not any hub's
internal message contract.

## Readiness Signal (new — `PendingMigrationsHealthCheck`)

A new `IHealthCheck` implementation, not a persisted entity:

| Property | Value |
|---|---|
| Input | `AskLucyDbContext.Database.GetPendingMigrationsAsync(cancellationToken)` |
| Healthy | Zero pending migrations |
| Unhealthy | One or more pending migrations (names included in the health check's `Data` dictionary for diagnosability) |
| Tag | `"ready"` — included in a new `/health/ready` mapping; the existing `/health` liveness endpoint (`Program.cs:588`) is unchanged and does not include this check, keeping liveness and readiness semantics distinct per constitution §14. |
