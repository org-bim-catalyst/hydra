# Phase 1 Data Model: Chat Loading & Reply Feedback Fixes

This feature introduces no new persisted entities, database schema, or API contracts (see
`research.md` and `plan.md` — `contracts/` is intentionally omitted). What follows are the
**UI view-state models** the render branches in `ConversationView` and `useChatStream` are
built around, since the spec's requirements are expressed entirely as state-driven display
rules.

## ConversationLoadState (derived, not new state — read from `useChatMessages`)

Represents what the chat area shows for the currently selected conversation.

| State | Condition | Renders |
|---|---|---|
| `NoConversationSelected` | `chatId === null` | The "Start a conversation with Ask Lucy." empty-state copy (FR-001) |
| `Loading` | `chatId !== null && isPending` | Loading spinner (`CircularProgress`), visible within 100ms of selection (FR-002, SC-002) |
| `Error` | `chatId !== null && isError` | Visible error state + manual "Retry" button calling `refetch()` (FR-004) |
| `Loaded` | `chatId !== null && !isPending && !isError` | The conversation's messages (existing virtualized list) |

**Transitions**: `NoConversationSelected` → `Loading` on conversation selection (new
`ConversationView` mount via `key` bump). `Loading` → `Loaded` on query success. `Loading` →
`Error` on query failure. `Error` → `Loading` on manual retry (`refetch()`). There is no
transition back to `NoConversationSelected` except via an explicit "New chat" action
(`chatId` becomes `null` again), matching FR-001's requirement that the empty state is never
reached as a side effect of a load failing or being in progress.

**Invariant**: Exactly one of these four states is rendered at any time — they are
mutually exclusive branches, not independent flags that could otherwise combine into a
contradictory display (e.g., spinner AND empty-state copy at once).

## ReplyState (derived, not new state — read from `useChatStream`'s existing per-message content + `isStreaming`)

Represents what a single assistant reply bubble shows while `useChatStream.send` runs.

| State | Condition | Renders |
|---|---|---|
| `Thinking` | `isStreaming && message.content === ''` | `ThinkingIndicator` (animated three dots), visible within 100ms of send (FR-006, SC-003) |
| `Streaming` | `isStreaming && message.content !== ''` | `MessageBubble` with partial content, updating per chunk |
| `Failed` | send threw before any content arrived | Placeholder bubble removed; error surfaced via the existing page-level Snackbar with a "Retry" action added (FR-008) |
| `Complete` | `!isStreaming && message.content !== ''` | `MessageBubble` with full content, no attribution caption (FR-009) |

**Transitions**: `Thinking` → `Streaming` the instant the first non-empty chunk is applied
to the message. `Thinking`/`Streaming` → `Failed` if `streamChat`/`ensureChatId` throws
(existing `catch` in `useChatStream.send`). `Streaming` → `Complete` when the stream
generator finishes. No minimum dwell time is enforced in `Thinking` (spec clarification) —
a response that fails or completes within a few milliseconds is allowed to skip visibly
occupying that state.

## Message-sync gate (`hasSentRef`, replaces the prior `initializedRef` — User Story 5, FR-012/FR-013)

`useChatStream` tracks, per mounted view, whether the user has sent anything in *this specific
view* — not whether the underlying query has ever returned a defined value. This single boolean
gates whether the view's local `messages` state keeps tracking `useChatMessages`' data:

| State | Condition | Behavior |
|---|---|---|
| `Following` | `!hasSentRef.current` | `messages` re-syncs from `initialMessages` on every change — including a corrected background refetch replacing an earlier incomplete/stale snapshot, and later-arriving paginated pages (FR-024). |
| `Diverged` | `hasSentRef.current` | `messages` is driven entirely by local state (the in-progress/completed send and its streamed reply); the query's data is no longer applied to this view, even if it changes. |

**Transition**: `Following` → `Diverged` happens exactly once per mount, at the start of `send`/
`sendImage`/`sendTranslation`, and never reverses within that mount — reopening the conversation
(a fresh mount) starts a new `Following` state from scratch. This is a one-way gate deliberately:
its purpose is to stop a same-view fetch from clobbering an active/completed local conversation,
not to stop legitimate updates before the user has acted.

## MessageBubble display fields (existing `ChatMessage` type — unchanged shape, changed usage)

`provider`/`model` remain on `ChatMessage`/`PersistedMessage` exactly as today (still
populated by the backend, still passed through `toChatMessages`) — only `MessageBubble`'s
render output changes (the attribution `<Typography>` caption is removed). No field is
renamed, removed, or added; this is a display-only change per FR-010.
