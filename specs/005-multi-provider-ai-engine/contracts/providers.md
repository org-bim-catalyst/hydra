# API Contract: Provider & Model Catalog (user-facing, read-only)

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New resource under `/api/v1/ai`, alongside the existing `AiController` routes
(`src/AskLucy.Web/Controllers/v1/AiController.cs`) — a new `AiProvidersController`. All
endpoints are `[Authorize]` and `[EnableRateLimiting("ai-catalog-endpoints")]` (a new,
lightweight, non-cost-tiered policy — research.md Decision 6; these are cacheable reads, not
AI-invoking calls, so they don't belong under the stricter `ai-endpoints` policy). Errors
follow RFC 7807 Problem Details (constitution §6). Every response excludes disabled
providers and non-`Available` models (FR-007) — this filtering happens server-side, never
left to the client to apply.

## List enabled providers

`GET /api/v1/ai/providers`

Response: `200 OK`, `ProviderSummaryDto[]` — `{ id, providerKey, displayName, healthStatus,
healthStatusCheckedAtUtc }` for every `AIProvider` where `IsEnabled = true`. Cached
server-side (constitution §15 — Performance) with a short TTL; the catalog changes only when
an administrator acts, not per request.

## List models for a provider

`GET /api/v1/ai/providers/{providerId}/models`

Response: `200 OK`, `ModelSummaryDto[]` — one entry per `AIModel` under `providerId` with
`Status = Available`: `{ id, modelKey, displayName, contextWindowTokens, maxOutputTokens,
capabilities: { streaming, vision, functionCalling, jsonMode, reasoning, embeddings,
imageInput, imageOutput, audio }, pricing: { inputPerMillionTokensUsd,
outputPerMillionTokensUsd } | null, releaseDate }` (FR-005). `pricing` is `null`, not zeros,
when pricing metadata is missing (FR-022). `404` if `providerId` doesn't exist or isn't
enabled.

## List all selectable models (flat, cross-provider)

`GET /api/v1/ai/models`

Convenience endpoint for a single "pick any model" UI control — same `ModelSummaryDto`
shape as above, plus `providerId`/`providerDisplayName`, across every enabled provider's
available models in one call. Equivalent to calling the per-provider endpoint for every
enabled provider, provided to avoid N+1 client-side requests (constitution §15).
