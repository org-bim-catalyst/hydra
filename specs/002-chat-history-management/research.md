# Research: Chat History & Conversation Management

**Feature**: [spec.md](./spec.md) | **Date**: 2026-07-29

All items below were design decisions needed to move from spec to data model/contracts;
none are open `NEEDS CLARIFICATION` markers — the stack itself is fixed by the existing
Ask Lucy solution (constitution §1/§3, CLAUDE.md), so research here is about *how* to use
that fixed stack for this feature's specific requirements, not *what* stack to use.

## Topic 1: Extending the existing chat entity vs. introducing a parallel "Conversation" concept

**Decision**: Extend the existing `UserChat`/`Message` aggregate (added in SPEC-000,
`src/AskLucy.Domain/Chats/`) in place, rather than introducing new `Conversation`/
`ConversationMessage` types. The spec's business vocabulary ("Conversation") maps onto
the already-persisted `UserChat` entity.

**Rationale**: Constitution §2.VII (Convention Over Configuration) and §3 require
following an established convention rather than introducing a parallel bespoke
mechanism. `UserChat` already carries the owner, title, audit fields, soft delete, and
concurrency token this feature needs; a rename/parallel-model would be pure churn with
no behavioral benefit and would violate DRY (§2.III).

**Alternatives considered**: A brand-new `Conversation` aggregate replacing `UserChat` —
rejected as a disruptive, unjustified rewrite of working SPEC-000 code with an EF Core
migration that would need to move existing rows/FKs for no functional gain.

## Topic 2: Modeling Archive, Pin, Favorite, and the "Recently Deleted" (Trash) state

**Decision**:
- **Archive** → new nullable `ArchivedAtUtc` column on `UserChat`. Presence = archived.
  Excluded from the default view by an *Application-layer* query condition (not a global
  EF query filter, since Archived items must remain visible under an explicit filter).
- **Pin** → new nullable `PinnedAtUtc` column. Presence = pinned; pinned conversations
  sort by `PinnedAtUtc DESC` ahead of all unpinned conversations (FR-008). Using a
  timestamp instead of a bare boolean gives pin ordering for free with no extra column.
- **Favorite** → new `IsFavorite bool` column (matches `docs/DATABASE.md` §6 Conversations
  schema, which lists `Favorite` as a flag, not a timestamp — no ordering requirement
  exists for favorites per spec.md, so a boolean is sufficient (YAGNI, §2.III)).
- **Recently Deleted (Trash)** → reuses the *existing* `DeletedAtUtc`/global-query-filter
  soft-delete convention (constitution §5 "Soft deletes & auditing") that `UserChat`
  already has via `BaseEntity`. Regular Delete (FR-003) sets `DeletedAtUtc`; the default
  global EF query filter (`DeletedAtUtc == null`) already hides it from every ordinary
  query. The "Recently Deleted" view (FR-020) is a dedicated query that explicitly
  bypasses the filter (`IgnoreQueryFilters()`), scoped to `DeletedAtUtc != null AND UserId
  == currentUser`, and Restore (FR-005a) simply clears `DeletedAtUtc`. This is exactly the
  mechanism the convention already provides — no new column needed for Trash.
- **Permanent Delete** → the *hard*-delete path the constitution already reserves:
  "hard delete is reserved for GDPR-style erasure requests via an explicit, audited
  command" (§5). Permanent Delete (FR-004) is implemented as that same audited,
  explicit-command hard-delete path (cascading to `Message`/`Attachment`/`Citation` rows),
  not a new mechanism.

**Rationale**: Every one of the four new states maps onto an existing constitutional
pattern (soft delete, GDPR hard delete) or a minimal, precedented column addition — no
new cross-cutting infrastructure, no ADR required.

**Alternatives considered**: A single `ConversationStatus` enum (Active/Archived/Deleted)
— rejected because Pin and Favorite are independent of Archive/Delete (a conversation can
be both archived and favorited per spec.md's Assumptions), so a mutually-exclusive enum
would not model the actual state space; a status column plus two independent booleans
would still need the same Pin/Favorite columns, so the enum adds nothing over dedicated
nullable-timestamp/bool columns for Archived/Deleted too — simpler to keep all four
independent.

## Topic 3: Duplicate (branch/fork) implementation

**Decision**: A single Application command (`DuplicateUserChatCommand`) creates a new
`UserChat` row and bulk-inserts copies of all existing `Message` rows (new `Guid` v7 ids,
same `UserChatId` = the new conversation's id, same ordering/content/metadata), committed
through one `IUnitOfWork.SaveChangesAsync()` call (constitution §5: "a business
transaction spans exactly one SaveChanges/Unit of Work commit").

**Rationale**: Matches the FR-010/clarification decision (full copy, true branch) and the
existing repository/Unit-of-Work pattern already used by `CreateUserChatCommandHandler`.

**Alternatives considered**: Database-level `INSERT ... SELECT` bulk copy for very large
conversations — deferred; not needed until a real performance problem is observed
(YAGNI), and the ORM-level bulk insert is adequate at the stated scale (SC-003: hundreds
of thousands of messages account-wide, not per single conversation).

## Topic 4: Auto-generated title algorithm (local, no AI call)

**Decision**: Derive the title from the first user message: strip Markdown/newlines,
collapse whitespace, and truncate to 60 characters at the nearest word boundary (with an
ellipsis if truncated). Runs synchronously in the same command that persists the first
message, so it satisfies SC-004 (within 1 second — effectively immediate, no external
call).

**Rationale**: Matches the clarification decision (no AI provider call). A fixed,
documented character budget keeps the title readable in the sidebar's fixed-width list
item without additional layout logic.

**Alternatives considered**: AI-provider-generated summary title — rejected per the
clarification session (adds cost/latency/failure mode).

## Topic 5: Message-content search

**Decision**: Use SQL Server Full-Text Search (a `CONTAINS`/`FREETEXT`-queryable full-text
index on `Message.Content` and `UserChat.Title`), scoped per-query to the requesting
user's own conversations. SQL Server FTS populates its index asynchronously
(change-tracking-based), which naturally satisfies the "near-real-time, within a few
seconds" freshness clarified for FR-019 without a bespoke indexing pipeline.

**Rationale**: The constitution already commits this project to SQL Server as the sole
data store, including for vector search (§5 "RAG & vector storage" — "no separate vector
database MAY be introduced without an ADR"); applying the same no-new-datastore
discipline to text search means using SQL Server's built-in full-text capability rather
than standing up Elasticsearch/OpenSearch, which would be new cross-cutting
infrastructure requiring an ADR (§17) the feature does not need.

**Alternatives considered**: A dedicated search engine (Elasticsearch) — rejected as
disproportionate new infrastructure for the stated scale (SC-001: 10,000 conversations)
and would require an ADR; plain `LIKE '%term%'` — rejected, since it cannot be indexed
efficiently and would violate the N+1/full-scan performance rule (§5, §15) at the
"hundreds of thousands of messages" scale target (SC-003).

## Topic 6: Pagination strategy

**Decision**: Cursor-based (keyset) pagination for both the conversation list and the
per-conversation message list. Cursor is an opaque, encoded composite of the active sort
column's value plus `Id` (as a tiebreaker) so pagination is stable even when new items
are inserted between page loads.

**Rationale**: Constitution §6 explicitly names this: "cursor-based for high-churn
collections like chat messages." The conversation list is included for the same reason
at the stated scale (SC-001: 10,000+ conversations) — offset pagination degrades and
produces duplicate/skipped rows under concurrent inserts.

**Alternatives considered**: Offset/skip-take pagination — constitution explicitly
reserves this for "small stable admin lists," which conversations/messages are not.

## Topic 7: Export file format

**Decision**: A single JSON document per export: conversation title, creation/update
timestamps, and an ordered array of messages (role, content, timestamp, provider/model,
token usage, generation parameters, and attachment/citation references as
filename+type+access-location per the export clarification) — no embedded binary content.

**Rationale**: JSON is structured, trivially versionable, and directly satisfies "suitable
for a future import capability to read" (FR-025) without inventing a bespoke schema
format; it requires no new library (System.Text.Json is already used platform-wide).

**Alternatives considered**: Markdown export — good for human reading but lossy for a
future machine-readable import (loses typed metadata like token counts/parameters);
deferred as a possible additional export option, not required by this spec.

## Topic 8: Frontend list/message virtualization

**Decision**: `@tanstack/react-virtual` for both the conversation sidebar list and the
per-conversation message list.

**Rationale**: Constitution §7/§15 mandate virtualization for long lists without
prescribing a library. The project already depends on TanStack Query; adopting the
TanStack Virtual package keeps the dependency family consistent (§2.III simplicity —
one vendor's virtualization primitive instead of introducing an unrelated one) and it
integrates cleanly with variable-height message bubbles.

**Alternatives considered**: `react-window` — a viable, lighter-weight alternative;
not chosen only to avoid a second, unrelated list-virtualization dependency alongside
TanStack Virtual once introduced.

## Topic 9: Optimistic UI updates & failure rollback

**Decision**: TanStack Query mutations for archive/restore/pin/favorite/duplicate/delete
use `onMutate` to optimistically patch the cached conversation list, with `onError`
rolling back to the previous cache snapshot and surfacing a toast — satisfying SC-005 and
constitution §2.VIII (No Silent Failures).

**Rationale**: This is the project's already-established data-fetching library
(CLAUDE.md, constitution §7); TanStack Query's built-in optimistic-update/rollback
primitives are the standard, already-adopted mechanism — no new pattern introduced.

## Topic 10: Concurrent-edit conflict handling

**Decision**: `DbUpdateConcurrencyException` from a stale `RowVersion` is caught in the
Application command handler and translated to a `409 Conflict` Problem Details response
(constitution §5 "handled explicitly at the Application layer, not left to bubble as a
500"; §6 Problem Details).

**Rationale**: Directly satisfies the spec's concurrent-edit edge case and is already the
constitution's mandated pattern — no new decision required beyond confirming it applies
here.
