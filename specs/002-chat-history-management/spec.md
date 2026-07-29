# Feature Specification: Chat History & Conversation Management

**Feature Branch**: `002-chat-history-management`

**Created**: 2026-07-29

**Status**: Draft

**Input**: User description: "Chat History & Conversation Management — transform Ask Lucy from a single-session chatbot into a modern AI workspace by introducing persistent conversations, conversation management, and a scalable chat architecture. Covers unlimited conversation creation, rename/delete/archive/restore/pin/favorite/duplicate/clear/export, rich message metadata (tokens, model, provider, generation parameters, attachments, citations), a searchable/filterable/sortable/paginated conversation sidebar, and support for millions of messages with virtual scrolling and optimistic UI. Excludes RAG, long-term memory, and AI agents (future specifications)."

## Clarifications

### Session 2026-07-29

- Q: Does "permanently delete" perform an irreversible hard-delete from the database, or is it a user-facing-irreversible action while data is retained for a time under the platform's existing soft-delete convention? → A: Irreversible for the user and hard-deleted immediately under the platform's existing GDPR-style audited-erasure convention (constitution §5) — not retained, no later purge step.
- Q: Does automatic title generation invoke the AI provider, or derive the title locally from the first message with no AI call? → A: Derived locally from the first message's text, with no AI provider call — zero added cost/latency, no new provider-availability failure mode.
- Q: Does duplicating a conversation copy all of its existing messages, or start as an empty conversation carrying over only the title/settings? → A: Full copy — the duplicate is an independent conversation containing a copy of every message up to the moment of duplication (a true branch/fork); the source conversation is unmodified.
- Q: When a user performs a regular Delete (not Permanent Delete) on a conversation, can they recover it afterward? → A: Yes — regular Delete moves the conversation to a user-visible "Recently Deleted" (Trash) view, from which the user can restore it or permanently delete it; items left there are not auto-purged.
- Q: Does exporting a conversation bundle the actual attachment file content, or just a reference to it? → A: Reference only — the exported file includes each attachment's filename, type, and existing access URL, not the file bytes themselves.
- Q: How fresh must message-content search results be relative to a message just sent? → A: Near-real-time — messages become searchable within a few seconds of being sent, not instantaneously, allowing an indexed/asynchronous search approach at scale.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Continue working across many conversations over time (Priority: P1)

A user starts a new conversation, exchanges messages with an AI model, closes the app or navigates away, and later returns — from the same device or a different one — to find every conversation exactly as they left it, with full message history and context intact, and can pick up any conversation exactly where they stopped.

**Why this priority**: This is the foundational capability the entire feature exists to deliver. Without reliable, complete persistence across sessions, none of the organizational features (search, pin, archive, etc.) have anything to operate on. It is the MVP.

**Independent Test**: Create several conversations, send messages in each, sign out (or reload), sign back in, and confirm every conversation and its full message history — content, order, and timestamps — is exactly as it was left.

**Acceptance Scenarios**:

1. **Given** a user has no existing conversations, **When** they start a new chat and send a message, **Then** a new conversation is created and the exchange is saved.
2. **Given** a user has multiple existing conversations, **When** they select a conversation from their list, **Then** its complete message history loads in the original order.
3. **Given** a user is in the middle of an active conversation, **When** they close and later reopen the application, **Then** the conversation and all prior messages are still present and selectable.
4. **Given** a user has an unlimited number of conversations, **When** they create a new one, **Then** the system does not reject or throttle creation due to an arbitrary conversation-count limit.

---

### User Story 2 - Organize and quickly find any conversation (Priority: P1)

A user with hundreds or thousands of past conversations needs to locate a specific one — by remembering something they discussed, the model/provider they used, or simply by browsing chronologically — without manually scrolling through an unbounded list.

**Why this priority**: Persistence without discoverability is not usable at scale. This is what makes the history in User Story 1 actually valuable once a user has more than a handful of conversations, and is required for the "millions of messages" scale target to be usable rather than merely stored.

**Independent Test**: Populate a user account with a large number of conversations spanning multiple dates, models, and providers; verify search, filters, and sort options each independently narrow the list to the expected results, and that scrolling through the full list remains responsive.

**Acceptance Scenarios**:

1. **Given** a user has many conversations, **When** they enter a search term matching a conversation title or message content, **Then** only matching conversations are shown.
2. **Given** a user applies a filter (Favorites, Archived, or Pinned), **When** the filter is active, **Then** only conversations matching that state are shown.
3. **Given** a user selects a sort order (newest, oldest, recently updated, alphabetical), **When** the list re-renders, **Then** conversations appear in the selected order.
4. **Given** a user has thousands of conversations, **When** they scroll down the sidebar, **Then** additional conversations load progressively without a noticeable delay or full-page reload.
5. **Given** the sidebar is grouped by date, **When** a user views the list, **Then** conversations are visually grouped under headings such as Today, Yesterday, Previous 7 Days, and older ranges.

---

### User Story 3 - Curate conversations with pin, favorite, archive, and duplicate (Priority: P2)

A user wants to keep their workspace tidy: pinning a conversation they're actively relying on to the top of the list, marking a valuable one as a favorite for easy return, archiving conversations they're done with (without losing them), restoring one they archived by mistake, or duplicating a conversation to branch off into a new direction while keeping the original intact.

**Why this priority**: These are the day-to-day curation actions that make long-term history genuinely manageable rather than a growing, undifferentiated pile. They build directly on User Stories 1 and 2 and are not required for the core save/find loop to work.

**Independent Test**: For a given conversation, pin it and confirm it moves to the pinned section; favorite it and confirm it appears in the Favorites filter; archive it and confirm it leaves the default view but appears under Archived; restore it and confirm it returns to the default view with its prior pin/favorite state intact; duplicate it and confirm a second, independent conversation is created.

**Acceptance Scenarios**:

1. **Given** an active conversation, **When** a user pins it, **Then** it appears in a pinned section ahead of unpinned conversations regardless of recency.
2. **Given** a pinned conversation, **When** a user unpins it, **Then** it returns to its normal chronological position.
3. **Given** any conversation, **When** a user marks it a favorite, **Then** it appears when the Favorites filter is applied.
4. **Given** an active conversation, **When** a user archives it, **Then** it disappears from the default conversation list and appears only under the Archived filter.
5. **Given** an archived conversation, **When** a user restores it, **Then** it reappears in the default conversation list.
6. **Given** any conversation with existing messages, **When** a user duplicates it, **Then** a new, independent conversation is created and the original is unchanged.
7. **Given** a conversation with messages, **When** a user clears its history, **Then** the conversation remains in the list (with its title) but has no messages, and this action is confirmed before it takes effect.

---

### User Story 4 - Delete conversations, including permanent removal (Priority: P2)

A user decides a conversation is no longer needed and removes it — either a routine delete they might later reconsider, or a deliberate, explicit permanent removal of conversations they want gone for good.

**Why this priority**: Deletion is a core lifecycle action users expect from day one of any chat product, but it is scoped after the curation actions in User Story 3 because it is destructive and depends on the same underlying conversation-state model.

**Independent Test**: Delete a conversation and confirm it no longer appears in the default list but does appear in Recently Deleted; restore it from there and confirm it returns to the default list; separately, invoke permanent deletion (directly or from Recently Deleted), confirm the system requires explicit confirmation before proceeding, and confirm the conversation and its messages are unrecoverable through any user-facing feature afterward.

**Acceptance Scenarios**:

1. **Given** an existing conversation, **When** a user deletes it, **Then** it no longer appears in their default conversation list but appears in a Recently Deleted view.
2. **Given** a conversation in Recently Deleted, **When** a user restores it, **Then** it reappears in the default conversation list with its prior pin/favorite/archive state intact.
3. **Given** a conversation in Recently Deleted, **When** a user leaves it there without acting, **Then** it remains available and is not automatically purged.
4. **Given** a user initiates permanent deletion of a conversation (from the default list or from Recently Deleted), **When** the system prompts for confirmation, **Then** the deletion only proceeds after the user explicitly confirms.
5. **Given** a user has confirmed permanent deletion, **When** the action completes, **Then** no user-facing feature (list, search, export, Recently Deleted, restore) can recover that conversation or its messages.
6. **Given** a user attempts to delete another user's conversation by any means, **When** the request is made, **Then** the system denies it.

---

### User Story 5 - Export a conversation (Priority: P3)

A user wants to save a copy of a conversation outside the application — for their records, to share the content elsewhere, or to keep a personal backup.

**Why this priority**: Valuable for portability and trust (users aren't locked in), but it is an auxiliary capability that depends on a conversation already being fully persisted (User Story 1) and is lower-impact day-to-day than organizing (User Story 2) or curating (User Story 3).

**Independent Test**: Export a conversation with a mix of text, attachments, and citations; confirm the exported file contains the complete message history, in order, along with the conversation's title and dates, with each attachment/citation represented by a reference rather than embedded file content, and that the file opens/reads correctly outside the application.

**Acceptance Scenarios**:

1. **Given** a conversation with message history, **When** a user exports it, **Then** they receive a downloadable file containing the full, ordered message history and the conversation's title and dates.
2. **Given** a conversation with no messages, **When** a user exports it, **Then** they receive a valid file reflecting the empty history rather than an error.
3. **Given** a conversation containing messages with attachments or citations, **When** a user exports it, **Then** the exported file references each attachment/citation (filename, type, and existing access location) rather than embedding the file content itself.
4. **Given** an exported conversation file, **When** it is opened outside the application, **Then** its structure is well-formed and suitable for a future import feature to read back in.

---

### User Story 6 - Automatic and manual conversation titles (Priority: P3)

A user starts a new conversation and, without having to name it themselves, sees a sensible descriptive title appear based on what the conversation is about — and can rename it manually at any time if they prefer something different.

**Why this priority**: Improves findability and polish but is not required for the core save/organize/delete loop to function; a conversation is fully usable with a generic default title until this is available.

**Independent Test**: Start a new conversation, send an initial message, and confirm a descriptive title is generated automatically shortly after; independently, manually rename a conversation and confirm the new title persists and is reflected everywhere it is displayed.

**Acceptance Scenarios**:

1. **Given** a brand-new conversation with no title yet set by the user, **When** the first exchange completes, **Then** the system assigns a descriptive title derived from that exchange.
2. **Given** any conversation, **When** a user edits its title manually, **Then** the manual title is preserved and is not overwritten by automatic title generation afterward.
3. **Given** a conversation's title (automatic or manual), **When** it is displayed anywhere in the interface, **Then** the same title is shown consistently everywhere.

---

### Edge Cases

- What happens when a user tries to archive, delete, or clear a conversation while an AI response is still actively streaming into it? The system must not corrupt the in-progress message; the action is either blocked until the stream completes or the in-progress content is safely finalized first.
- How does the system handle a search that matches nothing? It shows a clear "no results" state rather than an empty-looking list that could be mistaken for a loading or error state.
- What happens when a user duplicates a conversation? The duplicate is independent of pin, favorite, and archive state — it starts as a plain, unpinned, unarchived conversation regardless of the source's state.
- What happens when a user restores an archived conversation? It returns to the default view with whatever pin/favorite state it had before archiving.
- How does the system handle renaming a conversation to a blank or whitespace-only title? The rename is rejected and the previous title is retained.
- What happens when two devices or browser tabs modify the same conversation concurrently (e.g., one renames while another deletes)? The system detects the conflict and prevents one change from silently overwriting the other's effect.
- What happens when a user searches or filters using special characters or unusually long input? The system treats it as literal search text and returns correct results (or no results) without error.
- How does the system behave for a conversation containing an extremely large number of messages? The message view loads and scrolls smoothly by rendering only the visible portion rather than the entire history at once.
- What happens when a user attempts to permanently delete a conversation that is currently pinned or a favorite? The action is still permitted after confirmation; pin/favorite status does not protect a conversation from permanent deletion.
- What happens when a user deletes a conversation that is currently archived, pinned, or a favorite? It moves to Recently Deleted the same as any other conversation, and its prior archive/pin/favorite state is preserved for if/when it is restored.

## Requirements *(mandatory)*

### Functional Requirements

**Conversation lifecycle**

- **FR-001**: System MUST allow an authenticated user to create a new conversation with no limit on the total number of conversations they may have.
- **FR-002**: System MUST allow a user to rename any of their conversations at any time; a blank or whitespace-only title MUST be rejected.
- **FR-003**: System MUST allow a user to delete any of their own conversations; a deleted conversation MUST no longer appear in that user's default conversation list or default search results, and MUST instead appear in a Recently Deleted view.
- **FR-004**: System MUST allow a user to permanently delete a conversation (from the default list or from Recently Deleted) as an explicit, separately confirmed action, distinct from the routine delete in FR-003. Permanent deletion immediately and irreversibly removes the conversation and its messages from the database via the platform's existing GDPR-style audited hard-delete command; there is no retention window or later purge step for this action (distinct from the routine-delete/Recently-Deleted retention in FR-003/FR-005b).
- **FR-005**: System MUST require explicit user confirmation before a permanent deletion takes effect, and MUST NOT permanently delete a conversation without it.
- **FR-005a**: System MUST allow a user to restore a conversation from Recently Deleted back to the default conversation list, preserving its title, messages, and prior pin/favorite/archive state.
- **FR-005b**: System MUST NOT automatically purge conversations from Recently Deleted; they remain until the user restores or permanently deletes them.
- **FR-006**: System MUST allow a user to archive an active conversation, removing it from the default conversation view without deleting it.
- **FR-007**: System MUST allow a user to restore an archived conversation back to the default conversation view, preserving its title, messages, and prior pin/favorite state.
- **FR-008**: System MUST allow a user to pin a conversation so it is displayed ahead of unpinned conversations, and to unpin it again.
- **FR-009**: System MUST allow a user to mark a conversation as a favorite (independently of pin/archive state) and to remove that designation.
- **FR-010**: System MUST allow a user to duplicate a conversation, producing a new, independent conversation containing a copy of the source's messages up to the moment of duplication (a full branch/fork), without modifying the source conversation.
- **FR-011**: System MUST allow a user to clear all messages from a conversation while keeping the conversation itself (and its title) in their list, and MUST require confirmation before clearing.
- **FR-012**: System MUST record when each conversation was created and when it was last updated, and make both visible to the user.

**Titles**

- **FR-013**: System MUST automatically generate a descriptive title for a new conversation, derived locally from its initial message (no AI provider call), once the conversation has no user-assigned title.
- **FR-014**: System MUST NOT overwrite a manually-set title with an automatically generated one.

**Messages**

- **FR-015**: System MUST persist every prompt and every response in a conversation, preserving the exact order in which they occurred.
- **FR-016**: System MUST record, for each message, its timestamp, the AI provider and model that produced it (for assistant messages), the generation parameters used, and token usage.
- **FR-017**: System MUST persist any attachments and citations associated with a message alongside that message.
- **FR-018**: System MUST treat persisted messages as an immutable historical record — once saved, a message's content is not altered by later actions (only conversation-level state around it, such as archive/pin, can change).

**Discovery — search, filter, sort, pagination**

- **FR-019**: System MUST allow a user to search their own conversations by title and by message content, returning only conversations they own. Message-content search reflects messages within a few seconds of being sent (near-real-time); it is not required to be instantaneous.
- **FR-020**: System MUST allow a user to filter their conversation list by state: Favorites, Archived, Pinned, and Recently Deleted.
- **FR-021**: System MUST allow a user to sort their conversation list by newest, oldest, recently updated, and alphabetical order.
- **FR-022**: System MUST load large conversation lists incrementally (progressive/"infinite" loading) rather than requiring the entire history to load at once.
- **FR-023**: System MUST group conversations by recency (e.g., Today, Yesterday, Previous 7 Days, and older) when displayed in the default chronological view.
- **FR-024**: System MUST render long individual message histories using only the visible portion of the list, so that conversation length does not degrade scrolling performance.

**Export**

- **FR-025**: System MUST allow a user to export any of their own conversations, producing a complete, ordered copy of its messages plus its title and creation/update dates, in a structured file suitable for a future import capability to read. Attachments and citations referenced by messages are included by reference (filename, type, and existing access location), not as embedded file content.

**Security & access**

- **FR-026**: System MUST ensure a user can only view, search, modify, export, archive, restore, duplicate, clear, or delete conversations and messages that belong to them.
- **FR-027**: System MUST require an authenticated session for every conversation- and message-level operation.
- **FR-028**: System MUST log security-relevant events (e.g., repeated unauthorized access attempts to another user's conversations).

### Key Entities *(include if feature involves data)*

- **Conversation**: A user-owned, persisted chat thread. Attributes include owner, title (manual or auto-generated), the AI provider/model/system-prompt/parameters in effect, and state flags (archived, pinned, favorite, deleted/in Recently Deleted), plus creation and last-updated timestamps. Builds on the existing persisted chat entity already established for Ask Lucy; this feature extends it with the state flags, discovery, and lifecycle actions described above.
- **Message**: A single turn within a Conversation — either a user prompt or an assistant response. Attributes include role, ordered position, content, timestamp, token usage, generation parameters, and the provider/model that produced it (for assistant messages). Messages are immutable once created.
- **Attachment**: A file (or reference to one) associated with a specific message (e.g., an uploaded document or a generated image), retained as part of that message's permanent record.
- **Citation**: A reference associated with an assistant message pointing to a source that informed its content.
- **Conversation Export**: A point-in-time, structured snapshot of a conversation's title, dates, and full message history, produced on demand for a user to download. Attachments and citations are represented by reference (filename, type, existing access location), not embedded file content.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can locate a specific conversation among at least 10,000 owned conversations, by search or filter, in under 3 seconds.
- **SC-001a**: A message becomes findable via message-content search within a few seconds of being sent.
- **SC-002**: Users can resume any past conversation with its complete message history intact 100% of the time, across sessions and devices.
- **SC-003**: A user's conversation list remains responsive (scrolling and loading additional items with no perceptible stall) when the account holds hundreds of thousands of stored messages.
- **SC-004**: 100% of newly created conversations receive an automatically generated title within 1 second of the first message being sent, without the user needing to act.
- **SC-005**: Deleting, restoring from Recently Deleted, archiving, restoring from Archived, pinning, favoriting, or duplicating a conversation is reflected in the user's view immediately (optimistically), with any failure clearly surfaced and reversed if the underlying action does not succeed.
- **SC-006**: Zero instances of one user accessing, modifying, or viewing another user's conversation or message data, verified under test.
- **SC-007**: A user can export any conversation and successfully retrieve a complete, correctly ordered copy of it 100% of the time.
- **SC-008**: A user attempting permanent deletion is never able to complete it without an explicit confirmation step.

## Assumptions

- **Existing foundation**: Ask Lucy already persists a basic form of chat history (a saved, ownable chat entry with rename/delete and an append-only message log) as part of its prior modernization work. This feature extends that foundation with the fuller set of lifecycle states (archive/pin/favorite/duplicate/clear), discovery (search/filter/sort/pagination/grouping), richer message metadata (attachments, citations, generation parameters), and export — it does not replace or re-migrate what already exists.
- **Out of scope**: Retrieval-Augmented Generation, long-term memory, AI agents, multi-user conversation sharing/collaboration (a future "participant" concept), importing a previously exported conversation, and any subscription-tier storage/usage-quota enforcement are explicitly excluded from this feature and reserved for future specifications.
- **Single active AI provider today**: Only one AI provider/model is currently integrated; search/filter/sort by provider and model are built to be data-driven so they extend automatically as additional providers are added in a future specification, without requiring rework here.
- **Attachments and citations are persistence, not new capability**: This feature persists attachment and citation data associated with messages produced by existing capabilities (e.g., an uploaded file reference, a generated image, a translation source); it does not introduce new file-upload types or document-processing capability.
- **No automatic purge**: Archived conversations and conversations in Recently Deleted are retained indefinitely until the user takes further action (restore or permanent delete); there is no time-based automatic archival, restoration, or purge in this feature.
- **Auto-generated titles derive from conversation content already available to the system** (the initial exchange) rather than introducing a new, separate data source.
