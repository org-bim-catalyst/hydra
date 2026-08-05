# API Contract: Conversation Knowledge-Base Attachment & Retrieval Settings

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

New `ConversationKnowledgeBasesController`, nested under the existing `/api/v1/chats/{id}`
resource. Rate-limited via the existing `chat-endpoints` policy (these are conversation-management
operations, not search or indexing). Ownership enforced via the existing chat-ownership guard.

## Attach / detach knowledge bases

`PUT /api/v1/chats/{id}/knowledge-bases`

```json
{ "knowledgeBaseIds": ["kb-1", "kb-2"] }
```

Full-replace of the conversation's `ConversationKnowledgeBase` set (FR-035, US1 AC1–AC2, US3 AC5).
An empty array detaches all knowledge bases (FR-036 — subsequent messages perform no retrieval). A
`knowledgeBaseId` the caller does not own is rejected with `400 Bad Request` (never silently
dropped or silently included). Changes apply to messages sent after this call only; prior
messages' `RetrievalHistory`/`Citation` rows are untouched (FR-037, US3 AC5).

`GET /api/v1/chats/{id}/knowledge-bases` → `{ "knowledgeBaseIds": [...] }`.

## Retrieval settings

`PUT /api/v1/chats/{id}/retrieval-settings`

```json
{
  "searchMode": "Hybrid",
  "topK": 8,
  "similarityThreshold": 0.7,
  "maxContextTokens": 4000
}
```

(FR-020, FR-023, FR-024, US3 AC1–AC4). Any field may be `null` to revert that setting to the
system default. Same "applies to subsequent messages only" semantics as the attachment endpoint
above.

`GET /api/v1/chats/{id}/retrieval-settings` → the effective current settings, with each field
flagged `isSystemDefault: true/false` so the UI can distinguish an explicit override from an
inherited default.

## Retrieval outcome on a chat message

No new endpoint — surfaced as part of the existing streamed chat response
(`SendChatMessageCommand`/`StreamChunk`, specs/005). A final non-content `StreamChunk` (or an
appended message-metadata field, matching however citations/usage already ride along) carries:

```json
{
  "retrievalOutcome": "Unavailable",
  "citations": [],
  "retrievalError": {
    "type": "retrieval-unavailable",
    "detail": "The knowledge base search service is temporarily unavailable."
  }
}
```

(research.md Decision 8, FR-037a, US1 AC6). `retrievalOutcome` is one of `Grounded`,
`NoRelevantContent`, `Unavailable`, `NotApplicable` (no knowledge base attached). `citations` is
non-empty only for `Grounded`. `retrievalError` is present only for `Unavailable`, following the
same non-silent-failure shape (`type`/`detail`) as the existing `AiProviderUnavailableException` →
Problem Details mapping — the message itself is still returned to the user (degraded, not
blocked), per the clarified behavior.
