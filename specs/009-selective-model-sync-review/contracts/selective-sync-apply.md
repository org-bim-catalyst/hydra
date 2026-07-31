# Contract: Apply a confirmed sync diff (revised)

Amends spec 008's `contracts/admin-ai-models.md` "Apply a confirmed sync diff" section for
the same route. No other endpoint from spec 008 changes.

## Apply a (possibly partial) confirmed sync diff

`POST /api/v1/admin/ai/providers/{providerId}/models/actions/sync/apply`

Request: unchanged shape — `{ added: ProviderModelInfo[], removedFromVendor:
RemovedModelDto[] }`. **What's new**: the administrator's UI now populates these arrays
with only the rows they checked in the sync-review dialog, not necessarily the full diff
the `.../sync` call returned. An empty request (`added: []`, `removedFromVendor: []`) is
rejected — see Errors below (the UI already disables Confirm in this state per FR-008, but
the endpoint enforces it too, per the constitution's boundary-validation rule).

Response: **`200 OK`** with `ApplyProviderModelSyncResultDto` (see data-model.md) — this
replaces spec 008's `204 No Content`.

```json
{
  "appliedModelKeys": ["gpt-5", "gpt-5-mini"],
  "failed": [
    {
      "modelKey": "gpt-4-turbo",
      "displayName": "GPT-4 Turbo",
      "reason": "'gpt-4-turbo' already exists in the catalog — the diff is stale; re-run the sync check."
    }
  ]
}
```

Effect (FR-007/FR-007a/FR-007b, unchanged from spec 008 for rows that succeed):
- Each `added` entry that is **not** stale is created as a new `AIModel` row, status
  `Unavailable`.
- Each `removedFromVendor` entry that is **not** stale has its existing row's status set to
  `Unavailable`.
- No row is ever deleted.
- A stale row (an `added.modelKey` that already exists, or a `removedFromVendor.id` that
  doesn't belong to `providerId`) is skipped and reported in `failed` — **it no longer
  causes the whole request to fail** (spec 008's behavior). The rows that were not stale
  are still applied and committed in the same call.

## Errors

- `400` if both `added` and `removedFromVendor` are empty — "Nothing to apply."
- A stale row is **not** an error response — it's a per-row entry in the `200 OK` body's
  `failed` array (see above). This is the behavior change from spec 008, where a stale row
  produced a `400` for the entire request.
- All other error behavior (auth, provider-not-found) is unchanged from spec 008.

## Unchanged from spec 008

- `GET /api/v1/admin/ai/providers/{providerId}/models`
- `PATCH /api/v1/admin/ai/models/{id}`
- `POST /api/v1/admin/ai/providers/{providerId}/models/actions/sync` (the diff check itself)
