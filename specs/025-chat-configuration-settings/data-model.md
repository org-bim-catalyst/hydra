# Data Model: Chat Configuration in User Settings

**Feature**: [../spec.md](spec.md) | **Research**: [research.md](research.md)

This feature introduces no new persisted entities and no database migrations. It exposes
one existing, already-persisted pair of fields through a new read path, and introduces one
small piece of client-only, session-scoped state.

## Existing entity (unchanged) — `UserChat`

`src/AskLucy.Domain/Chats/UserChat.cs` — already has, and already persists, the fields this
feature needs to read:

| Field | Type | Notes |
|---|---|---|
| `ProviderId` | `Guid?` | The conversation's current AI provider — live FK, mutated only via `SetModelSelection(...)`. Already written by the existing `UpdateChatModelSelectionCommand`. |
| `ModelId` | `Guid?` | The conversation's current AI model — same as above. |

No schema change. No new EF Core migration.

## New response DTO — `ChatDetailDto`

`src/AskLucy.Application/Chats/Queries/GetChatById/ChatDetailDto.cs` (new file, per
research.md Decision 2):

| Field | Type | Source |
|---|---|---|
| `Id` | `Guid` | `UserChat.Id` |
| `Title` | `string` | `UserChat.Title` |
| `ProviderId` | `Guid?` | `UserChat.ProviderId` — `null` if the conversation has never had a model selection persisted (e.g., a brand-new chat with no messages sent yet). |
| `ModelId` | `Guid?` | `UserChat.ModelId` |

Validation/constraints: none beyond existing chat-ownership authorization
(`ChatOwnershipGuard`, reused from `UpdateChatModelSelectionCommandHandler`) — a caller may
only fetch a chat they own; otherwise `404 Not Found` (matching the existing pattern for
other single-chat operations on this controller, which resolve ownership before returning
any data).

## Client-only state — `activeConversationStore`

`src/features/chat/activeConversationStore.ts` (new file, per research.md Decision 1). Not
a domain entity — a UI-session concern, analogous to `voicePreferencesStore` in structure
but `sessionStorage`-backed rather than `localStorage`-backed (server-durable) since "which
conversation is currently open" is a session-lifetime concept, not a durable preference.

| Field | Type | Notes |
|---|---|---|
| `activeChatId` | `string \| null` | Mirrors `ChatPage`'s `selectedChatId`. `null` when no conversation is open (a fresh, unsaved new chat, or the user has never opened one this session). |

State transitions:
- Set by `ChatPage` whenever the user opens, switches to, or starts a conversation (mirrors
  existing `selectedChatId` transitions — no new transition logic, just a second place the
  same value is written).
- Read by the Chat Configuration hub (new component) to determine whether to render the
  current-conversation model control or the "no conversation currently open" state (spec
  Edge Cases).
- Cleared implicitly at the end of the browser session (`sessionStorage` lifetime) — no
  explicit "clear" action needed.

## Relocated, unmodified data (no changes)

These already-existing shapes are reused verbatim by relocated UI and require no changes:

- `ConversationSummaryDto` / `SearchUserChatsQuery` (paged conversation list) — powers the
  new standalone Chat History Settings tab, identical to today's in-workspace list.
- `AiProvider*` / `AiModel*` catalog types consumed by `useAiProviders`/`useAiModels` — power
  both the unchanged `AiProvidersTab` and the new current-conversation model control; already
  scoped to admin-enabled/active providers/models (spec FR-005).
- `voicePreferencesStore` and its backing `voiceApi.ts` — power the unchanged `VoiceTab`;
  Chat Configuration only links to it.
