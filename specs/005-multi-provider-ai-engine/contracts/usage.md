# API Contract: Usage & Cost (user-facing)

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Backs User Story 5 (FR-020–FR-023). Per-message usage needs no new endpoint — it already
rides along on the existing `GET /api/v1/chats/{id}/messages` response (`MessageDto` gains
`latencyMs`, `estimatedCostUsd`, `cachedTokenCount`, `reasoningTokenCount` alongside the
existing `inputTokenCount`/`outputTokenCount`/`provider`/`model` fields from
specs/002-chat-history-management). One new aggregate endpoint is added.

## Conversation usage summary

`GET /api/v1/chats/{chatId}/usage`

Part of the existing `ChatsController` (`[EnableRateLimiting("chat-endpoints")]` — an
aggregation read, not an AI-invoking call). `404` if the chat isn't the caller's.

Response: `200 OK`:
```
{
  "totals": { "inputTokens": n, "outputTokens": n, "cachedTokens": n,
              "reasoningTokens": n, "estimatedCostUsd": n | null },
  "byProviderModel": [
    { "providerId": "...", "modelId": "...", "displayName": "...",
      "messageCount": n, "inputTokens": n, "outputTokens": n,
      "estimatedCostUsd": n | null }
  ]
}
```
`estimatedCostUsd` at any level is `null` (not `0`) when one or more contributing messages
has no pricing data (FR-022) — the response also includes a `costIncomplete: boolean` flag
at the top level so the frontend can show "partial" rather than implying the total is exact
when some component costs are unknown.
