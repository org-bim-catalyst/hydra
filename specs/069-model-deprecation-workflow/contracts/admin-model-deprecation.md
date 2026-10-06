# Contract: Admin Model Deprecation API

All routes require the `admin.ai-providers.manage` permission (FR-023), return Problem Details on error (§6), and are rate-limited under the existing admin policy. Base: `/api/v1/admin/ai`. Amends [specs/009 selective-sync-apply](../../009-selective-model-sync-review/contracts/selective-sync-apply.md); every route not listed here is unchanged.

## 1. Sync diff, now with impact (amends the diff check)

`POST /providers/{providerId}/models/actions/sync`

Each `removedFromVendor` row gains `impact` (FR-003). Counts only, never identities or item contents.

```json
{
  "id": "…", "modelKey": "gpt-4-turbo", "displayName": "GPT-4 Turbo",
  "impact": {
    "platformDefaults": [
      { "targetKind": "ProviderDefault" },
      { "targetKind": "CapabilityAssignment", "capability": "Chat" }
    ],
    "proposedReplacement": { "id": "…", "displayName": "GPT-5", "missingCapabilities": [] },
    "eligibleReplacements": [
      { "id": "…", "displayName": "GPT-5", "missingCapabilities": [] },
      { "id": "…", "displayName": "GPT-5 mini", "missingCapabilities": ["Reasoning"] }
    ],
    "itemCounts": { "conversations": 12, "agents": 2, "prompts": 3, "workflowSteps": 1 },
    "distinctUserCount": 9
  }
}
```

- `proposedReplacement` is `null` when no candidate is eligible (shown as "no suitable replacement").
- `eligibleReplacements` is every Available model on the provider that is not itself being removed, best first (research D1). `missingCapabilities` lists flags the retired model has and the candidate lacks, which is how the admin override warning (FR-007) is built.
- The `added` side is unchanged.

## 2. Apply, now deprecating (amends the apply route)

`POST /providers/{providerId}/models/actions/sync/apply`

Request: each `removedFromVendor` entry gains an optional `replacement`. Omitted means `{ "kind": "Proposed" }`.

```json
{
  "added": [],
  "removedFromVendor": [
    { "id": "…", "modelKey": "gpt-4-turbo", "displayName": "GPT-4 Turbo",
      "replacement": { "kind": "Model", "modelId": "…" } }
  ]
}
```

`replacement.kind` is `Proposed`, `Model` (with `modelId`) or `None`.

Response `200 OK`: the 009 shape plus `deprecations`:

```json
{
  "appliedModelKeys": ["gpt-4-turbo"],
  "failed": [],
  "deprecations": [
    {
      "deprecationId": "…", "modelKey": "gpt-4-turbo",
      "replacement": { "id": "…", "displayName": "GPT-5" },
      "reassigned": [ { "targetKind": "ProviderDefault", "newModelId": "…" } ],
      "flagged": []
    }
  ]
}
```

Effect:
- A selected removed row is set to **Deprecated**, not Unavailable (FR-001), together with its record, default reassignments or flags, and the admin summary notices, in the one save (FR-005, FR-019).
- The user-item switch and user notices run afterwards as a background job (research D4). The response does not wait for them.
- A row fails (in `failed[]`, with a reason, rest still applied) when the model is stale, **or** when an explicit `Model` replacement is no longer Available, belongs to another provider, or is itself in this request's deprecation set.
- A `Proposed` replacement that went stale since the preview is re-ranked at confirm time, and `deprecations[].replacement` reports the model that was actually used.
- Errors unchanged: `400` when both arrays are empty, and `404` for an unknown provider.

## 3. List deprecations and open flags

`GET /deprecations?providerId={id}&awaitingValidation={bool}&openFlags={bool}&cursor=…`

Cursor-paged. Returns summaries (model, provider, replacement, validation status, counts of reassigned and open flags, `confirmedAtUtc`, `confirmedBy` display name).

`GET /deprecations/{deprecationId}` returns the full record (FR-002): reason, time, confirming admin, replacement and its source, validation state and who validated, every `PlatformDefaultChange` (with serving model and resolution), `userImpact` status, and `notifiedUserCount`. It never lists user identities.

`GET /providers` (existing) gains `openFlagCount` per provider for the AI Providers page banner (SC-009).

## 4. Validate a replacement (FR-011a / FR-011b)

- `POST /deprecations/{deprecationId}/actions/accept-replacement` returns `204`. It is allowed only while `AwaitingValidation`, and otherwise returns `409`.
- `POST /deprecations/{deprecationId}/actions/change-replacement` takes `{ "modelId": "…" }` and returns `202 Accepted` with a `ModelDeprecationBatch` id.

  It moves platform defaults at once, and moves items by a background job (the replacement-change batch). It is rejected with `400` when the model is not Available, is on another provider, or lacks a capability that a still-reassigned default requires. Such a default is listed under `flagged` instead when the admin passes `"acknowledgeLostCapabilities": true`.

## 5. Status endpoint, now locked (FR-021 / FR-022)

`PATCH /models/{id}` with `{ "status": … }`:
- Any change **from** Deprecated returns `409` Problem Details: "A deprecated model can't be re-enabled."
- Any change **to** Deprecated returns `409`: "Models are deprecated only through the sync review."
- Available ⇄ Unavailable is unchanged. Every rejection is logged as an admin action (FR-024).

## 6. Resolving a flag (FR-010)

No new route. Setting or clearing a provider default (`PATCH /providers/{id}` with `defaultModelId`) and upserting a capability assignment each resolve a matching open flag in the same save and record who, when, and which model. The response is unchanged.

## Run-time behavior (not an endpoint)

Runs that name a Deprecated model are resolved through `ExecutableModelResolver` (research D3):
- A model with an Available successor runs on it, and the output is attributed to the successor.
- A model with no usable successor fails the run with a user-safe message naming the retired model and how to choose another. Chat send returns `400` with that message, and agent and workflow steps fail with the same text. The failure is also recorded through spec 074's failure audit.
