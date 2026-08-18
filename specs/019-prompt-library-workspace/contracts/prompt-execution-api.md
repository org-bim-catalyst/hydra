# API Contract: Prompt Testing & Execution

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

Endpoints on `PromptsController` under `/api/v1/prompts/{id}/`, plus a top-level
`/api/v1/prompt-executions` for cross-prompt execution history. Same `[Authorize]` +
`PromptOwnershipGuard` posture as [prompts-api.md](./prompts-api.md). Rate limiting differs by
endpoint: `POST /api/v1/prompts/{id}/executions` invokes `IAIProvider` directly and uses the
existing cost-tiered `ai-endpoints` policy (same as chat send/voice reply); every other endpoint in
this contract (list/get/compare executions, test cases, ratings) uses the generous `prompt-endpoints`
policy, since none of them call an AI provider.

## Execute a prompt (streaming)

`POST /api/v1/prompts/{id}/executions` — `Content-Type: application/json` request,
`Content-Type: text/event-stream` response (research.md Decision 2, mirrors
`AiController`'s existing `StreamVoiceReplyCommand` SSE endpoint exactly).

Request:

```json
{
  "versionNumber": null,
  "variableValues": { "document": "...", "target_language": "French", "summary_length": "short" },
  "providerId": "...",
  "modelKey": "gpt-5-mini",
  "temperature": 0.3,
  "maxOutputTokens": 800,
  "structuredOutput": false,
  "useRagContext": true,
  "knowledgeBaseIds": ["..."],
  "useMemoryContext": false
}
```

`versionNumber: null` executes the current version (FR-040). Each SSE event carries a JSON-encoded
`PromptStreamChunk`:

```json
{ "contentDelta": "Ce document ", "usage": null }
```

...with a final chunk carrying `usage` once available (`{ "contentDelta": null, "usage": {
"inputTokenCount": 812, "outputTokenCount": 140, "latencyMs": 1830 } }`), the identical shape
`IAIProvider.StreamChatAsync`'s `StreamChunk` already returns.

**Pre-execution validation** (FR-013, User Story 2 AC1): if a required variable is missing or a
supplied value fails its type/length/format/allowed-value rule, the endpoint returns `400 Bad Request`
(`validation-failed`, with a per-variable `errors` extension) **before** opening the stream and
**before** any `IAIProvider` call — no partial/best-effort execution.

**Model-capability validation** (FR-004, User Story 2 AC4): if the requested `modelKey` doesn't
satisfy the prompt's `Required*` capability flags, `400 Bad Request` (`domain-rule-violation`) before
any provider call.

On completion, the handler persists `PromptExecution` (`Origin: TestingWorkspace`) +
`PromptExecutionResult` (output text, token counts, `CostEstimator`-derived `estimatedCostUsd`,
citations/memory-references when requested) and, on success, increments
`PromptUsageStatistics.SuccessfulExecutionCount` / updates `LastSuccessfulUseAtUtc`
(spec.md Clarifications — successful executions only). On an `AiProviderUnavailableException`/
`AiProviderRateLimitedException`/timeout, the SSE stream ends with a final `{ "error": {
"type": "...", "detail": "..." } }` event (never a silently truncated stream, FR-101/SC-010) and the
persisted `PromptExecution.Outcome = Failed` with a sanitized `ErrorDetail` — this does **not** count
toward usage.

## List / get executions

`GET /api/v1/prompts/{id}/executions?cursor=&pageSize=50` → `PromptExecutionSummaryDto[]`
(id, versionNumber, providerId, modelKey, outcome, latencyMs, estimatedCostUsd, createdAtUtc),
newest first, cursor-paginated (FR-042, User Story 2).

`GET /api/v1/prompt-executions/{executionId}` → full `PromptExecutionDetailDto` (resolved variable
values, output, token usage, cost, citations, memory references, rating if any) (FR-042, FR-045).

## Compare executions

`GET /api/v1/prompt-executions/compare?executionIds=...&executionIds=...` → an array of the full
detail DTOs above, side by side — each entry's provider/model/version/settings kept explicit so the
client can render the comparison view without re-deriving which execution used what (FR-045,
User Story 2 AC-comparison, SC-009).

## Rate an execution

`PUT /api/v1/prompt-executions/{executionId}/rating` `{ "value": "Good" }` (`Good` |
`NeedsImprovement` | `Failed`) — `204 No Content` (FR-044).

## Test cases

`POST /api/v1/prompts/{id}/test-cases` — body: `{ "name": "...", "variableValues": {...},
"expectedOutput": "...", "evaluationCriteria": "...", "providerId": "...", "modelKey": "...",
"sourceExecutionId": "..." }` (the last field optional — set when saved directly from a completed
execution, FR-043). `201 Created`.

`GET /api/v1/prompts/{id}/test-cases` → `PromptTestCaseDto[]` (FR-043).

`DELETE /api/v1/prompts/{id}/test-cases/{testCaseId}` → `204 No Content`.

## Error format

Same Problem Details posture as [prompts-api.md](./prompts-api.md) — `ai-provider-unavailable`,
`ai-provider-authentication-failed`, and `ai-provider-rate-limited` (already registered in
`ProblemDetailsMiddleware.cs`) are reused as-is for provider-side execution failures; no new
provider-error type is introduced (FR-046).
