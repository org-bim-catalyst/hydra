# Contract: Admin Provider Health API

**Feature**: `043-provider-error-classification`

All routes sit under the existing `AdminAiProvidersController`: `[Authorize(Policy = "AdministratorOrSuperUser")]`, `[EnableRateLimiting("admin-endpoints")]`, base route `/api/v1/admin/ai`. Error responses follow RFC 9457 Problem Details per [provider-failure-classification.md](./provider-failure-classification.md) §3.

---

## 1. `GET /api/v1/admin/ai/providers` — modified

Three fields added to each element. Additive only; no existing field changes shape, so this ships inside `v1` (constitution §6).

```json
[
  {
    "id": "0198e2c1-…",
    "providerKey": "google-gemini",
    "displayName": "Google Gemini",
    "isEnabled": true,
    "hasCredential": true,
    "credentialLastRotatedAtUtc": "2026-08-20T09:14:00Z",
    "defaultModelId": "0198e2c2-…",

    "healthStatus": "Unhealthy",
    "healthStatusCheckedAtUtc": "2026-08-29T09:07:49Z",

    "healthFailureKind": "QuotaExhausted",
    "healthFailureReason": "The provider reported its usage quota is exhausted. Credentials are valid.",
    "healthStaleAfterUtc": "2026-08-29T09:13:49Z"
  }
]
```

| Field | Type | Notes |
|---|---|---|
| `healthFailureKind` | `string \| null` | One of the nine kinds. Non-null **iff** `healthStatus == "Unhealthy"` |
| `healthFailureReason` | `string \| null` | Administrator-facing prose, ≤500 chars. Never a raw vendor body |
| `healthStaleAfterUtc` | `string \| null` | `healthStatusCheckedAtUtc + 3 × configured interval`. Null when never checked |

**Client staleness rule**: the status is presented as possibly out of date when `healthStaleAfterUtc != null && now > healthStaleAfterUtc`. Computing it client-side means an open page correctly turns stale without polling (research.md Decision 6).

**Presentation states** (FR-018/FR-020/FR-021), in precedence order:

| Condition | Presented as |
|---|---|
| `hasCredential == false` | **Not configured** — neutral, not a failure |
| `isEnabled == false` | Health suppressed — not presented as a failure |
| `healthStatus == "Unknown"` | **Not yet checked** — neutral, never red |
| `healthFailureKind` ∈ {`QuotaExhausted`, `RateLimited`} | **Configured, temporarily limited** — visually distinct from a credential failure |
| `healthStatus == "Unhealthy"`, other kinds | **Unhealthy**, with the reason |
| `healthStatus == "Healthy"` | **Healthy** |
| plus `now > healthStaleAfterUtc` | overlaid "possibly out of date" affordance on any of the above |

---

## 2. `POST /api/v1/admin/ai/providers/{providerId}/actions/check-health` — new

Performs one live probe now, writes one `ProviderHealthCheck` row, updates the provider's current state, returns the classified outcome. Mirrors the sibling `models/actions/sync` action form.

**Request**: no body.

**`200 OK`**

```json
{
  "healthStatus": "Healthy",
  "healthFailureKind": null,
  "healthFailureReason": null,
  "checkedAtUtc": "2026-08-29T14:22:10Z",
  "healthStaleAfterUtc": "2026-08-29T14:28:10Z"
}
```

A probe that finds the provider failing is still `200 OK` — the *check* succeeded; its finding is in the payload. Only a failure of the check mechanism itself is a non-2xx.

| Status | When |
|---|---|
| `200` | Probe completed; outcome in the body, healthy or not |
| `404` | No provider with that id |
| `429` | `admin-endpoints` rate limit (this is the FR-025 concurrency bound) |
| `403` | Principal is not an administrator |
| `500` | Failure of the check mechanism itself, e.g. the database is unreachable |

**Concurrency (FR-025)**: bounded by the existing per-user `admin-endpoints` rate-limit policy; no new mechanism. The UI additionally disables the trigger while the mutation is pending.

**Idempotency**: not idempotent by design — each call appends one history row, which is the audit record FR-026 requires.

---

## 3. `GET /api/v1/admin/ai/providers/{providerId}/models` — modified

Two fields become nullable. **Breaking for any client that assumed `number`**; the only consumer is this repository's own admin UI, updated in the same change.

```json
[
  {
    "id": "0198e2c3-…",
    "modelKey": "gpt-4-turbo",
    "displayName": "gpt-4-turbo",
    "contextWindowTokens": null,
    "maxOutputTokens": null,
    "status": "Unavailable"
  }
]
```

| Field | Was | Now |
|---|---|---|
| `contextWindowTokens` | `number` | `number \| null` |
| `maxOutputTokens` | `number` | `number \| null` |

`null` means **the vendor did not publish the figure**. It must render as *"Not published by the vendor"* and never as `0`, and — per FR-029a — must **not** reuse the word "Unknown", which this same table already uses for absent pricing.

`GET /api/v1/ai/providers/{id}/models` and `GET /api/v1/ai/models` (`ModelSummaryDto`) take the identical nullability change.

---

## 4. `POST .../models/actions/sync` and `.../sync/apply` — modified

`ProviderModelSyncDiff.added[]` carries the same two now-nullable fields, echoed back unchanged on apply.

**Behavioural change on apply**: a row whose limits are `null` is now **added successfully** rather than reported in `failed[]` with *"Context window must be greater than zero."* The `failed[]` array and its per-row reporting are unchanged and still used for genuinely stale rows (FR-031).

**Behavioural change on sync**: a provider-side failure now returns its specific classified Problem Details instead of a generic `500`. The dialog renders `problem.detail`, which for an administrator is now the specific message.

---

## 5. Frontend contract

`src/AskLucy.Web/ClientApp/src/features/admin/api/adminAiProvidersApi.ts`

```ts
export type ProviderFailureKind =
  | 'CredentialRejected' | 'CredentialUnreadable' | 'NotConfigured'
  | 'QuotaExhausted' | 'RateLimited' | 'UsageRestricted'
  | 'Unavailable' | 'RequestInvalid' | 'ResponseNotUnderstood'

export interface AdminAiProvider {
  // …existing fields…
  healthStatus: 'Unknown' | 'Healthy' | 'Unhealthy'
  healthStatusCheckedAtUtc: string | null
  healthFailureKind: ProviderFailureKind | null
  healthFailureReason: string | null
  healthStaleAfterUtc: string | null
}

export const checkProviderHealth = (providerId: string) =>
  apiFetch<CheckProviderHealthResult>(
    `/admin/ai/providers/${providerId}/actions/check-health`,
    { method: 'POST' },
  )
```

`ApiError` gains an optional `providerFailure` field populated from the Problem Details extension, so the admin UI can branch on `kind` for styling and on `canAdministratorAct` for the call-to-action, while continuing to render `detail` as the message text.

**Error handling (constitution §VIII)**: the new mutation must supply an `onError` that surfaces a visible message, matching the existing `ModelSyncDialog` Snackbar/Alert convention. No async call may be left uncaught.
