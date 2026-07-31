# API Contract: User AI Preferences

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Part of `AiProvidersController` (`/api/v1/ai/preferences`), `[Authorize]`,
`[EnableRateLimiting("ai-catalog-endpoints")]` (research.md Decision 6 — not AI-invoking).
Backs User Story 3 (FR-017, FR-019).

## Get current preferences

`GET /api/v1/ai/preferences`

Response: `200 OK`, `UserAiPreferenceDto`: `{ defaultProviderId, defaultModelId,
defaultGenerationParameters }`. If the caller has never saved preferences, returns the
platform-wide default provider/model (spec Assumption) with `isPlatformDefault: true` so the
frontend can distinguish "your saved choice" from "the fallback" (User Story 3, Acceptance
Scenario 1).

## Save preferences

`PUT /api/v1/ai/preferences`

Request: `{ defaultProviderId, defaultModelId, defaultGenerationParameters? }`. `400` if
`defaultModelId` doesn't belong to `defaultProviderId`, or if `defaultProviderId` isn't
currently enabled, or if any `defaultGenerationParameters` field isn't supported by
`defaultModelId` (mirrors the chat-endpoint validation in contracts/chat.md — FR-015/FR-016).
Response: `200 OK`, the saved `UserAiPreferenceDto`. Only affects **new** conversations
going forward (FR-017's Acceptance Scenario 2) — never retroactively changes an existing
conversation's `UserChat.ProviderId`/`ModelId`.
