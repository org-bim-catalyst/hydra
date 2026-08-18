# API Contract: Inserting a Prompt Into a Conversation

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)

One endpoint, added to the existing `ChatsController` (`/api/v1/chats/{chatId}/...`) rather than to
`PromptsController` — this action's contract is "send a chat message," not "manage a prompt"
(research.md Decision 4: delegates to the existing `SendChatMessageCommand`).

## Insert a prompt into an active conversation

`POST /api/v1/chats/{chatId}/prompt-messages`

Request:

```json
{
  "promptId": "...",
  "variableValues": { "document": "...", "target_language": "French" }
}
```

(FR-080, User Story 5). Behavior:

1. Resolves the prompt's variables against `variableValues` (FR-013 — same pre-execution validation
   as [prompt-execution-api.md](./prompt-execution-api.md); `400 Bad Request`
   (`validation-failed`) before anything is sent if a required variable is missing/invalid).
2. Checks the prompt's required capabilities against the conversation's **currently selected**
   model (FR-080 AC3); `400 Bad Request` (`domain-rule-violation`) if incompatible, surfaced to the
   user before the message is sent — the conversation's model selection is never silently overridden.
3. Composes the resolved prompt content into the conversation's next user message and delegates to
   the existing `SendChatMessageCommand` unchanged — provider/model selection, prior message context,
   RAG, memory, and streaming are all the existing chat pipeline's, not reimplemented here
   (research.md Decision 4).
4. On the chat send succeeding, records a `PromptExecution` row (`Origin: ConversationInsertion`,
   `ResultMessageId` = the newly created `Chats.Message.Id`) and increments
   `PromptUsageStatistics` (FR-051, spec.md Clarifications — successful executions only). On failure,
   no `PromptExecution` row is recorded successful and usage is not incremented.

Response: `200 OK`/streaming response — **identical shape** to `POST /api/v1/chats/{chatId}/messages`
(the existing send-message endpoint), since step 3 delegates to it directly. This endpoint adds no
new response contract of its own.

## Error format

Same Problem Details posture as the existing chat-message endpoint — no new Problem Details type is
introduced by this contract.
