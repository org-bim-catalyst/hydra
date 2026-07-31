# API Contract: Chat Generation & Model Comparison

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Revises the existing `POST /api/v1/ai/chat` (`AiController.Chat`) and adds a new
`POST /api/v1/ai/compare`. Both stay under the existing `[EnableRateLimiting
("ai-endpoints")]` policy already on `AiController` — no new rate-limit policy (research.md
Decision 6). Both remain `[Authorize]`. Errors follow RFC 7807 Problem Details, using the
three provider-error types from research.md Decision 9 (`ai-provider-unavailable`,
`ai-provider-authentication-failed`, `ai-provider-rate-limited`) — the same shape regardless
of which vendor produced the underlying failure (FR-028).

## Send a chat message (revised)

`POST /api/v1/ai/chat`

Request body gains three fields alongside the existing `chatId`/`messages`:
- `providerId` (Guid, required) — must reference an enabled `AIProvider` (`400` otherwise).
- `modelId` (Guid, required) — must reference an `Available` `AIModel` under `providerId`
  (`400` otherwise; this is also where FR-015's "parameter unsupported by this model" check
  runs — a `400` naming the specific unsupported parameter).
- `generationParameters` (object, optional) — any of temperature/topP/topK/
  presencePenalty/frequencyPenalty/maxTokens/stopSequences/seed/reasoningLevel/
  responseFormat/systemPrompt; omitted fields fall back to the conversation's
  `GenerationParametersJson` (FR-014), then the user's `UserAiPreference` defaults
  (FR-019), then provider defaults, in that order.

Response: unchanged wire shape — `text/event-stream`, `data: {chunk}\n\n` per token,
`data: [DONE]\n\n` to close (FR-012 — identical streaming behavior regardless of provider).
The persisted assistant `Message` (via the existing `AppendMessageCommand` composition in
the controller) now also stores `LatencyMs`, `EstimatedCostUsd`, `CachedTokenCount`,
`ReasoningTokenCount` alongside the existing `Provider`/`Model`/token-count fields
(FR-010/FR-020).

If the in-flight request is cancelled client-side (existing `AbortController` pattern in
`useChatStream.ts`), the server's `CancellationToken` (already threaded through
`SendChatMessageCommandHandler` → `IAIProvider.StreamChatAsync`) stops the upstream call
(FR-013) — no new endpoint needed. A connection drop mid-stream after partial content was
already sent is handled client-side per FR-030: the partial `assistantContent` accumulated
so far is preserved and flagged incomplete in the UI, not discarded (see quickstart.md
Scenario 6).

## Update a conversation's provider/model selection

`PATCH /api/v1/chats/{chatId}/model-selection`

New action on the existing chats resource (`ChatsController`, `[EnableRateLimiting
("chat-endpoints")]` — this doesn't itself invoke a provider, matching the existing
`ChatsController` policy assignment rationale in specs/002). Request body:
`{ providerId, modelId, generationParameters? }`. Applies to messages sent *after* this
call only (FR-009); prior messages keep their original `Message.Provider`/`Message.Model`
snapshot (FR-011). `404` if the chat isn't the caller's; `400` if `providerId`/`modelId`
aren't a valid enabled/available pair.

## Compare responses across models

`POST /api/v1/ai/compare`

Request: `{ chatId: Guid?, prompt: string, selections: [{ providerId, modelId }] }` — 2 to 5
selections (upper bound prevents unbounded fan-out cost; not specified numerically in the
spec, chosen as a reasonable operational guard — flagged for `/speckit-tasks` to confirm).
`400` if fewer than 2 selections, or if any selection references a disabled provider/
unavailable model (FR-024's edge case: excluded with a clear per-item reason, not a whole-
request failure).

Response: `200 OK` (non-streaming — research.md Decision 10), body:
```
{
  "comparisonId": "guid",
  "results": [
    { "providerId": "...", "modelId": "...", "displayName": "...",
      "content": "...", "usage": { ... } } |
    { "providerId": "...", "modelId": "...", "displayName": "...",
      "error": { "type": "ai-provider-unavailable", "detail": "..." } }
  ]
}
```
One entry per requested selection, always — a failed model never removes its slot, it
fills it with an `error` object instead (FR-026). **Nothing is persisted yet** — a
comparison the user never acts on leaves no trace in conversation history, only the act of
continuing does (below). This deliberately avoids a server-side ephemeral-result cache: the
client already holds every candidate's full content from this response, so `continue` (next)
resubmits it rather than the server needing to remember it.

## Continue from a comparison result

`POST /api/v1/ai/compare/{comparisonId}/actions/continue`

Request: `{ chatId: Guid, prompt: string, chosen: { providerId, modelId }, candidates:
[{ providerId, modelId, content } | { providerId, modelId, error }] }` — the same data the
`/compare` response just returned, plus which one the user picked.

Persists, in one command: the user's `prompt` as a single user `Message`; then one assistant
`Message` per successful `candidates[]` entry, each stamped with `ComparisonGroupId =
comparisonId` (data-model.md) and `IsIncludedInContext = (this candidate == chosen)`. This
satisfies FR-025 exactly: every successfully-generated comparison response becomes a real,
visible history entry (`IsIncludedInContext` only controls what future context-assembly
sends back to the provider, never what the UI displays), while only the chosen one feeds
subsequent turns. `Message` rows are still never mutated after creation (all are written
once, with their final `IsIncludedInContext` value already decided) — consistent with
`Message`'s existing append-only invariant.

Also updates the conversation's `ProviderId`/`ModelId` to the chosen selection (FR-025,
same mechanism as the model-selection `PATCH` above). `400` if `chosen` doesn't match a
successful (non-`error`) entry in `candidates`.
