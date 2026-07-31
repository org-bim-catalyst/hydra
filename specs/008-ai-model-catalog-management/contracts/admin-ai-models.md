# Contract: Admin AI Model endpoints (new)

Extends the existing `AdminAiProvidersController` (`[Authorize(Policy =
"AdministratorOrSuperUser")]`, `[EnableRateLimiting("admin-endpoints")]`) with the four
model-management routes spec 005's `contracts/admin.md` originally scoped out but never
built. Route shapes match that original contract; request/response bodies here are the
authoritative, as-implemented shapes (refined by this feature's clarifications).

## List a provider's models (any status)

`GET /api/v1/admin/ai/providers/{providerId}/models`

Response `200 OK`: `AdminAiModelDto[]` — every model for the provider, regardless of
status (FR-001). `404` if `providerId` doesn't exist.

## Change a model's status

`PATCH /api/v1/admin/ai/models/{id}`

Request: `{ status: "Available" | "Deprecated" | "Unavailable" }`. Response: `204 No
Content`. Any transition is allowed (FR-002) — no transition is blocked, since
deprecating/disabling a model already in use is exactly the scenario FR-004 (historical
attribution survives) exists to handle safely. `404` if the model doesn't exist.

## Check for a sync diff (read-only)

`POST /api/v1/admin/ai/providers/{providerId}/models/actions/sync`

No request body. Response `200 OK`: `ProviderModelSyncDiffDto` (see data-model.md).
**Never changes the catalog** (FR-006) — this is a query, not a mutation, despite the verb
(research.md Decision 3). Calls `IAIProvider.ListAvailableModelsAsync()` for the resolved
provider; on a provider-side failure, the existing `AiProviderUnavailableException`/
`AiProviderAuthenticationException`/`AiProviderRateLimitedException` → Problem Details
mapping applies unchanged — no new error type, no swallowed failure.

## Apply a confirmed sync diff

`POST /api/v1/admin/ai/providers/{providerId}/models/actions/sync/apply`

Request: the same `ProviderModelSyncDiffDto` shape the `.../sync` call returned, echoed
back by the client exactly as reviewed (no server-side cache of the proposal — same
pattern as spec 005's model-comparison "continue" endpoint). Response `204 No Content`.

Effect (FR-007/FR-008):
- Each entry in `added` is created as a new `AIModel` row, **status `Unavailable`**
  (Decision 2 — never immediately selectable; a separate `PATCH .../models/{id}` call is
  required to activate it).
- Each entry in `removedFromVendor` has its existing row's status set to `Unavailable`.
- No row is ever deleted.

`400` if any `removedFromVendor.id` doesn't belong to `providerId`, or if any `added`
entry's `modelKey` already exists for `providerId` (stale diff — the catalog changed
between the sync check and this call; the administrator should re-run the sync check).

## Error shape (all endpoints above)

RFC 7807 Problem Details, per the existing `ProblemDetailsMiddleware` — unchanged, no new
mapping required beyond what spec 005 already wired for `AiProvider*Exception` types.
