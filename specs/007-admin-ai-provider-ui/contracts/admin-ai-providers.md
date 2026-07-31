# Contract: Admin AI Provider endpoints this UI consumes

**These endpoints already exist** (delivered under `005-multi-provider-ai-engine`,
`src/AskLucy.Web/Controllers/v1/AdminAiProvidersController.cs`). This document is the
UI-facing contract reference for this feature — it does not introduce or change any
endpoint. All routes are `[Authorize(Policy = "AdministratorOrSuperUser")]` and
`[EnableRateLimiting("admin-endpoints")]`.

## List providers

`GET /api/v1/admin/ai/providers`

Response `200 OK`: `AdminAiProviderDto[]` — see data-model.md for the field list. Backs
User Story 1 (initial state) and User Story 2 (status-at-a-glance) in full; this feature
adds no query parameters.

## Enable / disable / change default model

`PATCH /api/v1/admin/ai/providers/{id}`

Request body: `{ isEnabled?: boolean, defaultModelId?: string | null }`. This feature only
ever sends `{ isEnabled: true }` or `{ isEnabled: false }` — `defaultModelId` is out of
scope here (model-catalog management, per spec Assumptions).

Response: `204 No Content` on success. `400` (Problem Details, `domain-rule-violation`)
if `isEnabled: true` is sent for a provider with `hasCredential: false` — the UI must
message this as "this provider needs a credential before it can be enabled" (FR-003) by
reading the Problem Details `detail` field, not a generic error.

## Set a provider's credential

`PUT /api/v1/admin/ai/providers/{id}/credential`

Request body: `{ apiKey: string }`. Response: `204 No Content`. Used for both User Story
1 (first-time set) and User Story 3 (replace/rotate) — the same endpoint covers both; the
UI does not need to distinguish "set" from "replace" when calling it.

Response never echoes the submitted value (SC-002) — there is no field to read one back
from even if the UI wanted to.

## Clear a provider's credential

`DELETE /api/v1/admin/ai/providers/{id}/credential`

No request body. Response: `204 No Content`. Per the existing backend implementation,
this always also disables the provider server-side (`AIProvider.ClearCredential`) — the
UI's confirmation copy for this action (FR-010) must say so up front, and the UI must
refetch/invalidate the provider list after this call so the row's `isEnabled` reflects the
now-forced-`false` state without the admin needing to notice it happened as a side effect.

## Error shape (all endpoints above)

RFC 7807 Problem Details (`application/problem+json`), per constitution §6 — already
enforced by the existing `ProblemDetailsMiddleware`. The UI reads `title`/`detail` for the
error message shown in the after-the-fact feedback required by FR-008; it does not
construct its own error copy from the HTTP status code alone.
