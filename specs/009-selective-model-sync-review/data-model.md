# Data Model: Selective Model Sync Review

No new database entities, migrations, or Domain changes. This feature adds one new
Application-layer response DTO and changes the internals (not the request shape) of one
existing command handler.

## Unchanged (from spec 008)

- `AIModel` (Domain) — no changes.
- `ProviderModelSyncDiffDto` / `ProviderModelInfo` / `RemovedModelDto` (Application) — the
  diff computation and its shape are untouched; the client simply sends a subset of these
  same shapes to apply.
- `ApplyProviderModelSyncCommand`'s **request** shape — still `{ providerId, added:
  ProviderModelInfo[], removedFromVendor: RemovedModelDto[] }`. What changes is that the
  administrator's UI now populates `added`/`removedFromVendor` with only the checked rows
  instead of always the full diff.

## New DTO (Application layer)

`ApplyProviderModelSyncResultDto` — the command's new return value (FR-007a/FR-007b):

| Field | Type | Notes |
|---|---|---|
| `appliedModelKeys` | `string[]` | The `modelKey` of every row (from either `added` or `removedFromVendor`) that was successfully applied in this call. |
| `failed` | `SyncApplyFailureDto[]` | One entry per row that was skipped because it was stale; empty when everything succeeded. |

`SyncApplyFailureDto`:

| Field | Type | Notes |
|---|---|---|
| `modelKey` | string | |
| `displayName` | string | |
| `reason` | string | Human-readable, e.g. `"'{modelKey}' already exists in the catalog — the diff is stale; re-run the sync check."` (research.md Decision 4). |

## Behavior change inside `ApplyProviderModelSyncCommandHandler` (no shape change to `AIModel`)

Per research.md Decision 2, the handler now performs the staleness check
**per row, before mutating**, instead of the validator rejecting the whole request when
any row is stale:

- For each `added` entry: if its `modelKey` already exists in the provider's catalog, add
  it to `Failed` with a reason and skip it (do not call `AIModel.Create`). Otherwise create
  it and immediately `SetStatus(Unavailable, actor)`, exactly as spec 008 already does, and
  record its `modelKey` in `AppliedModelKeys`.
- For each `removedFromVendor` entry: if its `id` doesn't resolve to a model belonging to
  this provider, add it to `Failed` with a reason and skip it. Otherwise `SetStatus
  (Unavailable, actor)` and record its `modelKey` in `AppliedModelKeys`.
- Exactly one `SaveChangesAsync` call commits every row that was not skipped (research.md
  Decision 2 — preserves the "one business transaction, one SaveChanges" rule).
- No row is ever deleted (unchanged from spec 008).

## Frontend-only state (not persisted, no Domain/Application entity)

- **Diff row selection**: a per-model-row checked/unchecked flag inside `ModelSyncDialog.tsx`
  local component state, keyed by `modelKey` (added side) or `id` (removed-from-vendor
  side). Exists only while the dialog is open for one sync-review session.
- **Filter text**: a single string, also local component state, applied client-side against
  `displayName`/`modelKey` on both diff sides simultaneously (FR-002).
