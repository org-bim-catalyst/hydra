# API Contract: Provider Administration

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New `AdminAiProvidersController`, `[Authorize(Policy = "AdministratorOrSuperUser")]`,
`[EnableRateLimiting("admin-endpoints")]` — the exact same shape as the existing
`AdminDashboardController` (`src/AskLucy.Web/Controllers/v1/AdminDashboardController.cs`).
Route prefix `/api/v1/admin/ai`. Backs User Story 1 (FR-001–FR-004), the admin half of User
Story 3/6 (FR-006, FR-027), and FR-023. Every mutating endpoint here is a security-sensitive
operation and MUST be written to the audit trail (constitution §8) — not just Serilog
application logs.

## Providers

`GET /api/v1/admin/ai/providers` — all providers (including disabled), with
`{ id, providerKey, displayName, isEnabled, hasCredential, credentialLastRotatedAtUtc,
defaultModelId, healthStatus, healthStatusCheckedAtUtc }`. Never includes the credential
value itself (FR-004/FR-031).

`PATCH /api/v1/admin/ai/providers/{id}` — body: `{ isEnabled?, defaultModelId? }`. `400` if
`isEnabled: true` is requested for a provider with no credential configured yet
(data-model.md validation rule). Audit-logged (who, when, before/after `isEnabled`).

`PUT /api/v1/admin/ai/providers/{id}/credential` — body: `{ apiKey: string }`. Encrypts and
stores via the existing `IDataProtectionProvider` pattern (research.md Decision 4); sets
`CredentialLastRotatedAtUtc = now`. Returns `204 No Content` — the value is never echoed
back, not even to the admin who just set it (FR-004). Audit-logged (who, when — never the
key value itself, per constitution §14's "no secrets in logs").

`DELETE /api/v1/admin/ai/providers/{id}/credential` — clears the credential and forces
`IsEnabled = false` (a provider cannot stay enabled with no credential). Audit-logged.

## Models

`GET /api/v1/admin/ai/providers/{providerId}/models` — all models for a provider, any
`Status`, with full capability/pricing detail (superset of the user-facing
`contracts/providers.md` endpoint, which filters to `Available` only).

`PATCH /api/v1/admin/ai/models/{id}` — body: `{ status: "Available" | "Deprecated" |
"Unavailable" }` (FR-006). Any transition in data-model.md's state diagram is allowed; no
transition is blocked, since deprecating/disabling a model already-in-use is exactly the
scenario FR-011 (historical attribution survives) exists to handle safely. Audit-logged.

`POST /api/v1/admin/ai/providers/{providerId}/models/actions/sync` — triggers
`IAIProvider.ListAvailableModelsAsync()` (research.md Decision 5) and returns a diff, not an
automatic write: `{ added: ModelSummaryDto[], removedFromVendor: ModelKeyDto[] }`. Applying
the diff (creating the `added` rows, marking `removedFromVendor` rows `Unavailable`) is a
separate, explicit `POST .../actions/sync/apply` call with the same diff payload echoed
back — a deliberate two-step confirm, consistent with FR-006 being an administrator action,
not an automatic background mutation of the selectable catalog.

## Health

`GET /api/v1/admin/ai/providers/{id}/health?take=50` — most recent `ProviderHealthCheck`
rows for a provider, newest first (FR-027, User Story 6 Acceptance Scenario 1–2).

## Usage & cost reporting

`GET /api/v1/admin/ai/usage?from={date}&to={date}&groupBy=provider|model` — FR-023.
Response mirrors `contracts/usage.md`'s per-user shape but aggregated across all users for
the given period: `{ totals: {...}, breakdown: [{ providerId, modelId?, requestCount,
inputTokens, outputTokens, estimatedCostUsd | null }] }`. `costIncomplete: boolean` at the
top level, same reasoning as the user-facing endpoint. `from`/`to` required, server-validated
date range (constitution §6 — no free-form query-to-SQL); a missing or excessively large
range (>1 year) is rejected `400` rather than silently running an unbounded aggregation.
