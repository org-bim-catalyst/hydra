# Data Model: Admin AI Model Catalog Management

No new database entities, migrations, or Domain changes — this feature is a new
Application/Web surface over the existing `AIModel` entity (`005-multi-provider-ai-engine`).
This document maps the spec's Key Entities onto what already exists and defines the new
DTOs this feature introduces.

## Existing Domain entity this feature reads/mutates (no changes)

`AIModel` (`src/AskLucy.Domain/Ai/AIModel.cs`) — already has everything this feature needs:
- `SetStatus(AIModelStatus status, string actor)` — any transition allowed (used by FR-002).
- `Create(...)` — used by the sync-apply path for genuinely new models (Decision 2:
  handler calls `Create` then immediately `SetStatus(Unavailable, actor)`).
- `Status` (`Available`/`Deprecated`/`Unavailable`), `IsSelectable` (`Status == Available`).

## New DTOs (Application layer)

`AdminAiModelDto` — the admin view of one model (FR-001). Adds `Status` to the existing
user-facing `ModelSummaryDto` shape (which deliberately omits `Status`, since end users
only ever see `Available` models):

| Field | Type | Notes |
|---|---|---|
| `id` | guid | |
| `modelKey` | string | |
| `displayName` | string | |
| `contextWindowTokens` | int | |
| `maxOutputTokens` | int | |
| `capabilities` | object | Same shape as `ModelSummaryDto.Capabilities`. |
| `pricing` | object? | `null`, never a fabricated zero, when unset (FR-001). |
| `releaseDate` | date? | |
| `status` | `"Available" \| "Deprecated" \| "Unavailable"` | |

`ProviderModelSyncDiffDto` — the read-only result of a sync check (FR-005/006), and also
the shape the client echoes back to `.../sync/apply` (no server-side ephemeral cache — the
client resubmits exactly what it reviewed, same pattern as spec 005's model-comparison
feature):

| Field | Type | Notes |
|---|---|---|
| `added` | `ProviderModelInfo[]` | Reuses the existing `Abstractions.ProviderModelInfo` record as-is (`ModelKey, DisplayName, ContextWindowTokens, MaxOutputTokens, Capabilities`) — the vendor's own reported shape, no pricing (vendors don't report it). |
| `removedFromVendor` | `{ id: guid, modelKey: string, displayName: string }[]` | Enough for the UI to display and for apply to target by `id`; no vendor data needed for this side. |

## State transitions (unchanged — restated for traceability to spec.md)

- `Available` ⇄ `Deprecated` ⇄ `Unavailable`, any direction, admin-triggered (FR-002) —
  `AIModel.SetStatus` already permits any transition (data-model.md from spec 005:
  "any transition is allowed").
- A model added via a confirmed sync starts `Unavailable` (FR-008, Decision 2) — an
  administrator must separately call `SetStatus(Available, ...)` (FR-002) to activate it.
- A model marked `Unavailable` because the vendor stopped listing it (sync-apply,
  "removedFromVendor" side) is never deleted — it can still be manually reinstated
  (`SetStatus(Available, ...)`) if that turns out to be wrong, same as any other status
  change.
