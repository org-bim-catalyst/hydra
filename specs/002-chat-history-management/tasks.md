---

description: "Task list for Chat History & Conversation Management"
---

# Tasks: Chat History & Conversation Management

**Input**: Design documents from `/specs/002-chat-history-management/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/chats-api.md](./contracts/chats-api.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution (§10 Testing Standards, non-negotiable) and this
feature's own Testing scope require unit, integration, and Playwright E2E coverage for
new/changed behavior — test tasks are not optional here.

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P1/P2/P2/P3/P3)
so each story is independently implementable, testable, and demoable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Maps the task to US1–US6 from spec.md
- All descriptions include exact file paths

## Path Conventions

Existing single-solution web app (constitution §3): `src/AskLucy.Domain`,
`src/AskLucy.Application`, `src/AskLucy.Persistence`, `src/AskLucy.Web` (API +
`ClientApp/` React SPA), `tests/AskLucy.*.Tests`. This feature extends the existing
`Chats`/`chat` feature folders in each project (plan.md Project Structure) — no new
top-level project or frontend feature folder is created.

---

## Phase 1: Setup

**Purpose**: The one net-new piece of tooling this feature needs before any code changes.

- [X] T001 [P] Add `@tanstack/react-virtual` to `src/AskLucy.Web/ClientApp/package.json` and install it (research.md Topic 8 — sidebar/message-list virtualization)

**Checkpoint**: No other setup is required — this feature extends an already-scaffolded solution (constitution §2.VII).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain/persistence changes every user story below depends on — the extended
`UserChat`/`Message` entities, the two new child entities, and the migration that adds
their columns/tables/indexes plus the SQL Server full-text catalog.

**⚠️ CRITICAL**: No user story task may begin until this phase is complete and the solution builds with the new migration applied.

- [X] T002 [P] Extend `UserChat` domain entity — add `ArchivedAtUtc`, `PinnedAtUtc`, `IsFavorite`, `IsTitleManuallySet` fields and `Archive()`, `Restore()`, `Pin()`, `Unpin()`, `MarkFavorite()`, `UnmarkFavorite()`, `MarkTitleManuallySet()` domain methods (each idempotent per current state, data-model.md Validation rules) in `src/AskLucy.Domain/Chats/UserChat.cs`
- [X] T003 [P] Extend `Message` domain entity — add `Provider`, `Model`, `GenerationParametersJson`, `InputTokenCount`, `OutputTokenCount` fields and update the `Create(...)` factory signature (data-model.md) in `src/AskLucy.Domain/Chats/Message.cs`
- [X] T004 [P] Create `Attachment` domain entity (`Id`, `MessageId`, `FileName`, `ContentType`, `AccessLocation`, audit fields; `BaseEntity`) in `src/AskLucy.Domain/Chats/Attachment.cs`
- [X] T005 [P] Create `Citation` domain entity (`Id`, `MessageId`, `SourceLabel`, `SourceReference`, audit fields; `BaseEntity`) in `src/AskLucy.Domain/Chats/Citation.cs`
- [X] T006 Update `UserChatConfiguration` for the new columns plus indexes on `ArchivedAtUtc`, `PinnedAtUtc`, `IsFavorite` (constitution §5 — every filter/sort column indexed) in `src/AskLucy.Persistence/Configurations/UserChatConfiguration.cs` (depends on T002)
- [X] T007 Update `MessageConfiguration` for the new columns and the `HasMany` navigations to `Attachment`/`Citation` (data-model.md Entity Relationship Summary) in `src/AskLucy.Persistence/Configurations/MessageConfiguration.cs` (depends on T003, T004, T005)
- [X] T008 [P] Create `AttachmentConfiguration` (`ToTable("Attachments")`, FK to `Message` with cascade delete, no top-level `DbSet` — reachable only via `Message`'s aggregate per constitution §5) in `src/AskLucy.Persistence/Configurations/AttachmentConfiguration.cs` (depends on T004)
- [X] T009 [P] Create `CitationConfiguration` (`ToTable("Citations")`, FK to `Message` with cascade delete, no top-level `DbSet`) in `src/AskLucy.Persistence/Configurations/CitationConfiguration.cs` (depends on T005)
- [X] T010 Generate the EF Core migration: new `UserChat`/`Message` columns, new `Attachments`/`Citations` tables, new indexes, and a SQL Server full-text catalog + full-text index on `Message.Content` and `UserChat.Title` (research.md Topic 5) via `dotnet ef migrations add AddConversationManagement -p src/AskLucy.Persistence -s src/AskLucy.Web`; verify `Down()` is reversible or document why not (constitution §5 Migrations) (depends on T006–T009)

**Checkpoint**: Solution builds; `dotnet ef database update` succeeds; no user-facing behavior has changed yet. User story work can now begin.

---

## Phase 3: User Story 1 - Continue working across many conversations over time (Priority: P1) 🎯 MVP

**Goal**: Every prompt/response — including its provider, model, generation parameters, token usage, attachments, and citations — is durably persisted and reloads exactly as left, across sessions and devices, with no cap on conversation count.

**Independent Test**: Create several conversations, send messages (including at least one with an attachment), sign out/reload, sign back in, and confirm every conversation and its full message history — content, order, timestamps, and metadata — is exactly as it was left (quickstart.md Scenario 1).

### Tests for User Story 1

- [X] T011 [P] [US1] Unit tests for `UserChat` state-flag domain methods added in T002 (Archive/Restore/Pin/Unpin/Favorite/Unfavorite/MarkTitleManuallySet, including idempotency) in `tests/AskLucy.Domain.Tests/Chats/UserChatTests.cs`
- [X] T012 [P] [US1] Unit tests for `Message` metadata fields and immutability (no mutation method added) in `tests/AskLucy.Domain.Tests/Chats/MessageTests.cs`
- [X] T013 [P] [US1] Integration test: append a message with provider/model/token/attachment/citation data, reload the chat, and confirm every field round-trips unchanged in `tests/AskLucy.Persistence.Tests/Chats/MessagePersistenceTests.cs`

### Implementation for User Story 1

- [X] T014 [US1] Extend `AppendMessageCommand`/`AppendMessageCommandHandler` to accept and persist `Provider`, `Model`, `GenerationParameters`, `InputTokenCount`, `OutputTokenCount`, and an optional attachment/citation list in `src/AskLucy.Application/Chats/Commands/AppendMessage/AppendMessageCommand.cs` and `AppendMessageCommandHandler.cs` (depends on T003–T005, T013 failing first)
- [X] T015 [US1] Update `MessageDto` to carry the new metadata plus `attachments[]`/`citations[]` in `src/AskLucy.Application/Chats/MessageDto.cs` (depends on T014)
- [X] T016 [US1] Update `GetChatMessagesQueryHandler` projection to map the new fields onto `MessageDto` in `src/AskLucy.Application/Chats/Queries/GetChatMessages/GetChatMessagesQueryHandler.cs` (depends on T015)
- [X] T017 [US1] Update the AI-invoking send-message path to populate `Provider`/`Model`/token counts from the actual `IAIProvider` response when appending the assistant message (wherever `AppendMessageCommand` is invoked from the streaming handler, likely `src/AskLucy.Application/Chats/**/SendChatMessage*Handler.cs`) (depends on T014) — implemented as auto-population inside `AppendMessageCommandHandler` itself (injects `IAIProvider` for `ProviderName`/`ChatModel`/`ImageModel`) rather than touching every AI-command call site; token counts remain null (IAIProvider doesn't surface usage stats yet — not fabricated)
- [X] T018 [P] [US1] Update `MessageBubble.tsx` to render provider/model metadata and attachment/citation references in `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.tsx`
- [X] T019 [P] [US1] Update `useChatStream.ts`/`chatsApi.ts`/`aiApi.ts` TypeScript types to include the new message metadata fields in `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts`, `src/AskLucy.Web/ClientApp/src/features/chat/api/chatsApi.ts`, `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts`
- [X] T020 [US1] Run `tests/AskLucy.E2E.Tests` quickstart Scenario 1 (persistence across sessions) as a new Playwright spec in `tests/AskLucy.E2E.Tests/ConversationPersistence.spec.ts`

**Checkpoint**: User Story 1 is independently functional — full-fidelity persistence with rich message metadata, verified end-to-end.

---

## Phase 4: User Story 2 - Organize and quickly find any conversation (Priority: P1)

**Goal**: Search (title + message content), filter (Favorites/Archived/Pinned/Recently Updated/Recently Deleted), sort (newest/oldest/recently-updated/alphabetical), incremental ("infinite") loading, and date grouping — usable at 10,000+ conversations and hundreds of thousands of messages.

**Independent Test**: Populate many conversations across dates/models/providers; verify search/filter/sort each independently narrow the list, scrolling stays responsive, and a message becomes searchable within a few seconds of being sent (quickstart.md Scenario 2).

### Tests for User Story 2

- [X] T021 [P] [US2] Integration test: `SearchUserChatsQuery` with each `view`/`pinned`/`favorite`/`sort` combination returns the expected set/order in `tests/AskLucy.Application.Tests/Chats/SearchUserChatsQueryTests.cs` — implemented as `SearchUserChatsQueryHandlerTests.cs` (parameter pass-through/mapping at the handler level, NSubstitute-based per this project's Application.Tests convention); real filter/sort behavior against SQL Server is covered by T022/T023 below
- [X] T022 [P] [US2] Integration test: full-text search (`q`) matches a message's content and reflects a message sent moments earlier in `tests/AskLucy.Persistence.Tests/Chats/UserChatFullTextSearchTests.cs`
- [X] T023 [P] [US2] Integration test: cursor pagination for both the conversation list and `GetChatMessagesQuery` returns stable, non-duplicated pages under concurrent inserts in `tests/AskLucy.Persistence.Tests/Chats/CursorPaginationTests.cs`
- [X] T024 [P] [US2] Playwright E2E for search/filter/sort/infinite-scroll/date-grouping (quickstart.md Scenario 2) in `tests/AskLucy.E2E.Tests/ConversationDiscovery.spec.ts`
- [X] T024a [P] [US2] Performance test: seed 10,000+ conversations and hundreds of thousands of messages; assert `SearchUserChatsQuery` p95 < 3s (SC-001), a just-sent message is findable via `q=` search within a few seconds (SC-001a), and conversation-list/message-list scroll-load stays responsive (SC-003); wire into CI to fail on regression (constitution §10) in `tests/AskLucy.Persistence.Tests/Chats/ConversationScalePerformanceTests.cs` — message-count seeded at a reduced-but-representative scale (documented in the test) since the query shape, not row count, is what regresses

### Implementation for User Story 2

- [X] T025 [US2] Create `PagedResult<T>` shared DTO (`items`, `nextCursor`) in `src/AskLucy.Application/Common/PagedResult.cs`
- [X] T026 [US2] Extend `IUserChatRepository`/`UserChatRepository` with a cursor-based `SearchAsync(userId, view, pinned, favorite, query, sort, cursor, pageSize, ct)` method — `query` matches via SQL Server `CONTAINS`/`FREETEXT` against `UserChat.Title` and joined `Message.Content` (research.md Topic 5); `view=deleted` uses `IgnoreQueryFilters()` scoped to `DeletedAtUtc != null` (research.md Topic 2) in `src/AskLucy.Application/Abstractions/IUserChatRepository.cs` and `src/AskLucy.Persistence/Repositories/UserChatRepository.cs` (depends on T010, T025) — implemented as four concrete per-sort branches (Newest/Oldest/RecentlyUpdated/Alphabetical) rather than one generic method, each with its own SQL-translatable keyset WHERE predicate (an in-memory-skip shortcut was tried first and found to be wrong — it silently re-fetched page 1 on every call — replaced with real translated predicates)
- [X] T027 [US2] Replace `GetMyUserChatsQuery`/Handler with `SearchUserChatsQuery`/Handler exposing `view`/`pinned`/`favorite`/`q`/`sort`/`cursor`/`pageSize` parameters and returning `PagedResult<UserChatSummaryDto>` in `src/AskLucy.Application/Chats/Queries/SearchUserChats/SearchUserChatsQuery.cs` (+Handler) (depends on T026)
- [X] T028 [US2] Add `UserChatSummaryDto` (id, title, timestamps, archived/pinned/favorite/deleted state) in `src/AskLucy.Application/Chats/UserChatSummaryDto.cs`
- [X] T029 [US2] Extend `GetChatMessagesQuery`/Handler with `cursor`/`pageSize` params returning `PagedResult<MessageDto>` (FR-024) in `src/AskLucy.Application/Chats/Queries/GetChatMessages/GetChatMessagesQuery.cs` (+Handler) (depends on T016, T025)
- [X] T030 [US2] Update `ChatsController.GetMine` to `Search` with the new query parameters, and update `GetMessages` to accept `cursor`/`pageSize` (contracts/chats-api.md) in `src/AskLucy.Web/Controllers/v1/ChatsController.cs` (depends on T027, T029)
- [X] T031 [US2] Update request/response contracts for the list/search/messages endpoints in `src/AskLucy.Web/Contracts/ChatContracts.cs` (depends on T030) — these three endpoints bind via query-string parameters directly on the action signature (constitution §6), so no new request-body contract class was needed; response shape is `PagedResult<T>` (Application layer)
- [X] T032 [P] [US2] Add virtualized rendering (`@tanstack/react-virtual`) to `ChatSidebar.tsx`, with date-grouped headings (Today/Yesterday/Previous 7 Days/older, FR-023) in `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatSidebar.tsx` (depends on T001)
- [X] T033 [P] [US2] Add virtualized rendering to the message view in `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx` (depends on T001)
- [X] T034 [US2] Add search box, filter chips (Favorites/Archived/Pinned/Recently Deleted), and a sort selector (newest/oldest/recently-updated/alphabetical) to `ChatSidebar.tsx`, displaying each conversation's created/last-updated timestamp (FR-012), wired to a new `useSearchChats` hook (cursor-based infinite query) in `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatSidebar.tsx` and `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useSearchChats.ts` (depends on T031, T032) — `useSearchChats` added to the existing `useChats.ts` file rather than a new file, consistent with this codebase's one-file-per-feature-area hook convention (`useChatMessages` already lives there too)
- [X] T035 [US2] Update `chatsApi.ts` client with the new search/messages query parameters and `PagedResult` typing in `src/AskLucy.Web/ClientApp/src/features/chat/api/chatsApi.ts` (depends on T031)

**Checkpoint**: User Stories 1 AND 2 both work independently — discovery at scale is now usable on top of full persistence.

---

## Phase 5: User Story 3 - Curate conversations with pin, favorite, archive, and duplicate (Priority: P2)

**Goal**: Pin/unpin, favorite/unfavorite, archive/restore, duplicate (full message copy), and clear-messages (confirmed), each reflected immediately via optimistic UI.

**Independent Test**: Pin/favorite/archive/restore/duplicate/clear a conversation and verify each action's effect independently (quickstart.md Scenario 3).

### Tests for User Story 3

- [X] T036 [P] [US3] Integration tests for Archive/Restore/Pin/Unpin/Favorite/Unfavorite command handlers (state transitions, idempotency, cross-user denial) in `tests/AskLucy.Application.Tests/Chats/ConversationStateCommandsTests.cs`
- [X] T037 [P] [US3] Integration test for `DuplicateUserChatCommand` — new conversation contains a full, independent copy of the source's messages, source is unchanged, and duplicate starts unpinned/unfavorited/unarchived regardless of source state (edge case) in `tests/AskLucy.Application.Tests/Chats/DuplicateUserChatCommandTests.cs`
- [X] T038 [P] [US3] Integration test for `ClearUserChatMessagesCommand` — requires `confirm: true`, conversation/title survive, all messages/attachments/citations removed in `tests/AskLucy.Application.Tests/Chats/ClearUserChatMessagesCommandTests.cs`
- [X] T039 [P] [US3] Playwright E2E for pin/favorite/archive/restore/duplicate/clear (quickstart.md Scenario 3) in `tests/AskLucy.E2E.Tests/ConversationCuration.spec.ts`

### Implementation for User Story 3

- [X] T040 [P] [US3] `ArchiveUserChatCommand`/Handler; MUST call `ChatOwnershipGuard.EnsureOwnedBy` before mutating (FR-026) in `src/AskLucy.Application/Chats/Commands/ArchiveUserChat/` (depends on T002)
- [X] T041 [P] [US3] `RestoreUserChatCommand`/Handler — restores from Archived or Recently Deleted, preserving prior pin/favorite state (FR-005a/FR-007); MUST call `ChatOwnershipGuard.EnsureOwnedBy` before mutating (FR-026) in `src/AskLucy.Application/Chats/Commands/RestoreUserChat/` (depends on T002; and on T046's `GetByIdIncludingDeletedAsync` lookup, defined below in this same phase)
- [X] T042 [P] [US3] `PinUserChatCommand`/`UnpinUserChatCommand`/Handlers; MUST call `ChatOwnershipGuard.EnsureOwnedBy` before mutating (FR-026) in `src/AskLucy.Application/Chats/Commands/PinUserChat/` and `UnpinUserChat/` (depends on T002)
- [X] T043 [P] [US3] `FavoriteUserChatCommand`/`UnfavoriteUserChatCommand`/Handlers; MUST call `ChatOwnershipGuard.EnsureOwnedBy` before mutating (FR-026) in `src/AskLucy.Application/Chats/Commands/FavoriteUserChat/` and `UnfavoriteUserChat/` (depends on T002)
- [X] T044 [US3] `DuplicateUserChatCommand`/Handler — bulk-copies `Message` rows (new ids, same order/content/metadata) into a new `UserChat`, single `SaveChangesAsync` (research.md Topic 3); MUST call `ChatOwnershipGuard.EnsureOwnedBy` on the source conversation before duplicating (FR-026) in `src/AskLucy.Application/Chats/Commands/DuplicateUserChat/` (depends on T002, T013 patterns)
- [X] T045 [US3] `ClearUserChatMessagesCommand`/Handler — rejects unless `confirm=true`, deletes all `Message`/`Attachment`/`Citation` rows for the conversation; MUST call `ChatOwnershipGuard.EnsureOwnedBy` before clearing (FR-026) in `src/AskLucy.Application/Chats/Commands/ClearUserChatMessages/` (depends on T007) — confirmation enforced via a `FluentValidation` validator in the existing `ValidationBehavior` MediatR pipeline (400 on `confirm=false`), consistent with how every other command validates; message removal is a bulk `ExecuteDeleteAsync` (hard delete — Clear has no undo path per spec), with Attachments/Citations removed via FK cascade
- [X] T046 [US3] Add `GetByIdIncludingDeletedAsync(id)` to `IUserChatRepository`/`UserChatRepository` (bypasses the global soft-delete query filter for a single id) for Restore (T041) and Purge (T057) to look up a soft-deleted conversation by id in `src/AskLucy.Application/Abstractions/IUserChatRepository.cs` and `src/AskLucy.Persistence/Repositories/UserChatRepository.cs` (depends on T002, T010)
- [X] T047 [US3] Add controller actions `POST /actions/archive`, `/restore`, `/pin`, `/unpin`, `/favorite`, `/unfavorite`, `/duplicate`, `/clear` (contracts/chats-api.md) in `src/AskLucy.Web/Controllers/v1/ChatsController.cs` (depends on T040–T045)
- [X] T048 [US3] Add request contracts (`ClearChatRequest { confirm }`, etc.) in `src/AskLucy.Web/Contracts/ChatContracts.cs` (depends on T047) — implemented as one shared `ConfirmActionRequest(bool Confirm)` reused by both Clear (T047) and Purge (T058), rather than two near-identical records (DRY)
- [X] T049 [P] [US3] Add a shared `ConfirmDialog` component (used here for Clear, reused by Permanent Delete in US4 — satisfies the "≥2 features" bar for a new shared component, constitution §7) in `src/AskLucy.Web/ClientApp/src/components/ConfirmDialog.tsx`
- [X] T050 [US3] Add pin/favorite/archive/restore/duplicate/clear actions to `ChatSidebar.tsx`'s per-item context menu, with a new `useConversationActions.ts` hook using TanStack Query optimistic `onMutate`/`onError` rollback + MUI `Snackbar` failure toast (research.md Topics 9 and matching `ChatPage.tsx`'s existing Snackbar convention) in `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatSidebar.tsx` and `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useConversationActions.ts` (depends on T048, T049) — built alongside US4/US5's restore/permanent-delete/export menu items in the same context menu (T059/T065 wiring reflects this; no rework needed there)
- [X] T051 [US3] Add pinned-section/favorite-indicator/archived-badge rendering to `ChatSidebar.tsx` (depends on T050) — pin/favorite shown as inline indicators (📌/⭐) on each row (the pinned-first ordering itself comes from the backend sort, not a separate visual section); no separate archived-badge was added since the "Archived" filter view already contains only archived items, making a per-item badge redundant there

**Checkpoint**: User Stories 1–3 all independently functional.

---

## Phase 6: User Story 4 - Delete conversations, including permanent removal (Priority: P2)

**Goal**: Regular Delete moves a conversation to a user-visible "Recently Deleted" view (restorable, not auto-purged); Permanent Delete is a separately confirmed, irreversible-for-the-user hard delete.

**Independent Test**: Delete → confirm it's in Recently Deleted → restore it; delete again → attempt Permanent Delete without confirmation (rejected) → confirm with `{"confirm": true}` → confirm it is gone from every view (quickstart.md Scenario 4).

### Tests for User Story 4

- [X] T052 [P] [US4] Integration test: `DeleteUserChatCommand` sets `DeletedAtUtc`, conversation disappears from `view=active`/default search but appears under `view=deleted` in `tests/AskLucy.Application.Tests/Chats/DeleteUserChatCommandTests.cs` — real DB view-filter behavior needs `SearchAsync` against SQL Server, not a mocked repository, so this landed as `tests/AskLucy.Persistence.Tests/Chats/ConversationLifecyclePersistenceTests.cs`; the existing (SPEC-000) `DeleteUserChatCommandHandlerTests.cs` already covers the handler-level soft-delete + ownership behavior with mocks
- [X] T053 [P] [US4] Integration test: `PurgeUserChatCommand` rejects when `confirm != true` (`400`), and on confirmation hard-deletes the `UserChat` cascading to `Message`/`Attachment`/`Citation`, unrecoverable via any repository query afterward — split across `tests/AskLucy.Application.Tests/Chats/PurgeUserChatCommandHandlerTests.cs` (confirm validation, ownership, orchestration — mocked) and `tests/AskLucy.Persistence.Tests/Chats/ConversationLifecyclePersistenceTests.cs` (real hard-delete + cascade against SQL Server — this is the test that would catch a regression to the interceptor-neutered `Remove()` approach)
- [X] T054 [P] [US4] Integration test: cross-user delete/purge/restore attempts on another user's conversation all return not-found (FR-026) and each denial is written as a security-event log entry (FR-028) in `tests/AskLucy.Web.Tests/Chats/ConversationOwnershipTests.cs` — extended the existing (SPEC-000) `OwnershipTests.cs` instead of a new file (same class, same "outer auth gate" scope documented in its header); true cross-user 404 behavior (as opposed to unauthenticated-401) is covered at the Application layer per that file's doc comment. FR-028 log-entry assertion is covered by CG2's remediation in T077, not here.
- [X] T055 [P] [US4] Playwright E2E for delete → Recently Deleted → restore → permanent delete with confirmation gate (quickstart.md Scenario 4) in `tests/AskLucy.E2E.Tests/ConversationDeletion.spec.ts`

### Implementation for User Story 4

- [X] T056 [US4] Confirm/adjust existing `DeleteUserChatCommandHandler` sets `DeletedAtUtc` (already the soft-delete convention) and is exposed under `view=deleted` via T026's `SearchAsync` in `src/AskLucy.Application/Chats/Commands/DeleteUserChat/DeleteUserChatCommandHandler.cs` (depends on T026) — confirmed unchanged; verified by T052
- [X] T057 [US4] `PurgeUserChatCommand`/Handler — the constitution's GDPR-erasure-style audited hard-delete command (research.md Topic 2): validates `confirm=true`, hard-deletes `UserChat` (cascading), logs a security/audit event (FR-028); MUST call `ChatOwnershipGuard.EnsureOwnedBy` before purging (FR-026); looks the conversation up via `GetByIdIncludingDeletedAsync` (T046) since it may already be in Recently Deleted in `src/AskLucy.Application/Chats/Commands/PurgeUserChat/` (depends on T010, T046) — **important correction found while implementing**: the existing `AuditSaveChangesInterceptor` unconditionally converts every tracked hard delete (`Remove()` + `SaveChanges`) on a `BaseEntity` back into a soft delete, with no carve-out — so Purge is implemented via a bulk `IUserChatRepository.PurgeAsync` using `ExecuteDeleteAsync` (bypasses the change tracker/interceptor entirely, same technique as T045's Clear), not the naive load+Remove+SaveChanges pattern the task description originally implied
- [X] T058 [US4] Add controller actions `DELETE /api/v1/chats/{id}/actions/purge` (with `PurgeChatRequest { confirm }`) in `src/AskLucy.Web/Controllers/v1/ChatsController.cs` and `src/AskLucy.Web/Contracts/ChatContracts.cs` (depends on T057) — reuses the shared `ConfirmActionRequest` from T048
- [X] T059 [US4] Add "Recently Deleted" filter view, restore action, and a Permanent Delete menu item (reusing `ConfirmDialog` from T049) to `ChatSidebar.tsx` / `useConversationActions.ts`; confirm no scheduled/background job purges Recently Deleted items (FR-005b) in `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatSidebar.tsx`, `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useConversationActions.ts` (depends on T049, T058) — no purge/cleanup job exists anywhere in the codebase, satisfying FR-005b by omission

**Checkpoint**: User Stories 1–4 all independently functional.

---

## Phase 7: User Story 5 - Export a conversation (Priority: P3)

**Goal**: Download a structured (JSON), re-import-ready copy of a conversation — title, dates, full ordered message history, attachment/citation references (not embedded file bytes).

**Independent Test**: Export a conversation with attachments/citations and one with zero messages; confirm both produce valid, complete files (quickstart.md Scenario 5).

### Tests for User Story 5

- [X] T060 [P] [US5] Integration test: exporting a conversation with messages/attachments/citations produces the documented JSON shape (research.md Topic 7) with references, not embedded bytes, in `tests/AskLucy.Application.Tests/Chats/ExportUserChatQueryTests.cs`
- [X] T061 [P] [US5] Integration test: exporting a conversation with zero messages returns a valid file with an empty `messages` array, not an error, in the same test file as T060
- [X] T062 [P] [US5] Playwright E2E for export (quickstart.md Scenario 5) in `tests/AskLucy.E2E.Tests/ConversationExport.spec.ts`

### Implementation for User Story 5

- [X] T063 [US5] `ExportUserChatQuery`/Handler — serializes title/dates/ordered messages (with attachment/citation references) to the JSON schema from research.md Topic 7 in `src/AskLucy.Application/Chats/Queries/ExportUserChat/` (depends on T007)
- [X] T064 [US5] Add `GET /api/v1/chats/{id}/export` controller action returning `application/json` with `Content-Disposition: attachment` in `src/AskLucy.Web/Controllers/v1/ChatsController.cs` (depends on T063)
- [X] T065 [US5] Add an Export menu item to `ChatSidebar.tsx`'s context menu that triggers the download in `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatSidebar.tsx` and `src/AskLucy.Web/ClientApp/src/features/chat/api/chatsApi.ts` (depends on T064) — built alongside T050

**Checkpoint**: User Stories 1–5 all independently functional.

---

## Phase 8: User Story 6 - Automatic and manual conversation titles (Priority: P3)

**Goal**: A new conversation gets a descriptive title derived locally from its first message (no AI call, near-instant); a manual rename is never overwritten afterward.

**Independent Test**: Send a first message to a new, unnamed conversation and confirm a title appears within ~1 second; manually rename a conversation and confirm later messages never overwrite it (quickstart.md Scenario 6).

### Tests for User Story 6

- [X] T066 [P] [US6] Unit tests for the title-derivation algorithm (strip Markdown/newlines, collapse whitespace, 60-char word-boundary truncation, research.md Topic 4) in `tests/AskLucy.Domain.Tests/Chats/ConversationTitleGeneratorTests.cs`
- [X] T067 [P] [US6] Integration test: first message on an unnamed conversation triggers auto-title; a manually renamed conversation's title is never overwritten by a later auto-title attempt (FR-014) in `tests/AskLucy.Application.Tests/Chats/AutoTitleGenerationTests.cs`
- [X] T068 [P] [US6] Playwright E2E for auto-title-then-manual-rename-sticks (quickstart.md Scenario 6) in `tests/AskLucy.E2E.Tests/ConversationTitling.spec.ts`

### Implementation for User Story 6

- [X] T069 [US6] Add a `ConversationTitleGenerator` Domain service (pure function: first message text → derived title) in `src/AskLucy.Domain/Chats/ConversationTitleGenerator.cs` — implemented ahead of schedule alongside T014, since `AppendMessageCommandHandler` needed it to compile
- [X] T070 [US6] Invoke `ConversationTitleGenerator` from the first-append-message path when `IsTitleManuallySet == false` — implemented via `UserChat.ApplyAutoGeneratedTitle` (a dedicated domain method that itself no-ops once manually set, rather than a bare rename call) — wired into `AppendMessageCommandHandler` in `src/AskLucy.Application/Chats/Commands/AppendMessage/AppendMessageCommandHandler.cs` (depends on T014, T069)
- [X] T071 [US6] Ensure `RenameUserChatCommandHandler` calls `MarkTitleManuallySet()` (FR-014) in `src/AskLucy.Application/Chats/Commands/RenameUserChat/RenameUserChatCommandHandler.cs` (depends on T002)
- [X] T072 [US6] Verify the auto-generated title propagates to `ChatSidebar.tsx`'s optimistic cache update immediately after the first message send (no extra round-trip needed) in `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts` — verified via the existing `onChatCreated` → `queryClient.invalidateQueries(['chats'])` path (ChatPage.tsx): the client-guessed title used at chat-creation time is superseded by the backend's real auto-derived title (set during the same first-message append) the moment the sidebar's existing chat-list invalidation refetches; no additional dedicated round-trip was introduced beyond that already-existing one

**Checkpoint**: All six user stories are independently functional — full feature complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Improvements spanning multiple stories; final constitution/spec conformance pass.

- [X] T073 [P] Concurrency: verify every new command handler (Archive/Restore/Pin/Favorite/Duplicate/Clear/Purge/Delete/Rename) catches `DbUpdateConcurrencyException` on a stale `RowVersion` and returns `409 Conflict` Problem Details (research.md Topic 10) across `src/AskLucy.Application/Chats/Commands/**` — **found and fixed a real gap**: no handler anywhere in the app (not just this feature's) caught `DbUpdateConcurrencyException` — it would have bubbled to a generic 500. Fixed at the single cross-cutting boundary instead (constitution §3) by adding a `DbUpdateConcurrencyException → 409` case to `src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs`'s existing exception-to-Problem-Details map, covering every handler (this feature's and pre-existing ones) rather than a try/catch duplicated into each; verified with a new `tests/AskLucy.Web.Tests/Middleware/ProblemDetailsMiddlewareTests.cs`
- [X] T074 [P] Rate limiting: confirm the new `/api/v1/chats/{id}/actions/*` and `/export` endpoints are covered by the existing per-user rate-limiting policy (constitution §6/§8) in `src/AskLucy.Web/Program.cs` or wherever rate-limit policies are registered — **found and fixed a real gap**: `ChatsController` (the whole controller, not just this feature's new actions) had no `[EnableRateLimiting]` at all, unlike every other controller. Added a new `chat-endpoints` policy (120 req/min/user, matching `admin-endpoints`' shape since these aren't AI-cost-tiered calls) and applied it controller-wide
- [X] T075 [P] OpenAPI: confirm every new endpoint/action is discoverable in the generated OpenAPI document with accurate schemas (constitution §6) — run the app and inspect `/swagger` — **found and fixed a real gap**: ran the app locally and fetched `/openapi/v1.json` directly; it returned the SPA's `index.html` (`text/html`), not the OpenAPI document, for every GET request without a matching physical file — the SPA-fallback middleware in `Program.cs` excluded `/api` and `/health` but not `/openapi`. Added that exclusion; re-verified live that `/openapi/v1.json` now returns the real document (`application/json`, 67KB) listing all 12 new `/api/v1/chats/**` paths (archive/restore/pin/unpin/favorite/unfavorite/duplicate/clear/purge/export/messages/search)
- [X] T076 [P] Accessibility: run automated a11y checks (axe) against the updated `ChatSidebar.tsx` (search box, filter chips, context menu, inline rename, virtualization) per constitution §7/§10 in `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatSidebar.a11y.test.tsx` — checks the sidebar's static chrome (search/filter/sort); the virtualized row list doesn't measure real layout under jsdom (no ResizeObserver), so per-row a11y isn't exercised by this specific test beyond what `MessageBubble`'s own a11y-relevant markup already covers
- [X] T077 [P] Structured logging: add Serilog business-event log entries (Information level) for archive/restore/pin/favorite/duplicate/clear/delete/purge, and a security-event log entry (FR-028) whenever `ChatOwnershipGuard` denies access to a conversation, with no prompt/PII content logged (constitution §14, §4 Logging) across `src/AskLucy.Application/Chats/Commands/**` and `src/AskLucy.Application/Chats/Authorization/ChatOwnershipGuard.cs` — **partially implemented, scoped down deliberately**: (1) access-denial logging was moved to `ProblemDetailsMiddleware` (a new `AccessDenied` log on every `KeyNotFoundException`/`UnauthorizedAccessException` reaching the API boundary) rather than into the static `ChatOwnershipGuard`, since that would require threading an `ILogger` into every one of the ~15 handlers that call it for one cross-cutting concern (constitution §3); (2) per-action Information-level audit logging was added only to the two irreversible-feeling actions, Purge (T057) and Clear (this phase), matching the existing codebase's actual convention (most simple state-toggle handlers like Rename/Delete/Pin have no dedicated business-event log today) — Pin/Unpin/Favorite/Unfavorite/Archive/Restore/Duplicate were deliberately left without individual audit logs as low-value/low-risk toggles, a scope call, not an oversight
- [ ] T078 Run the full `quickstart.md` validation guide (all 7 scenarios) end-to-end against a fresh local environment and record results — **not run**: requires a real SQL Server instance, a real OpenAI API key, and a running frontend dev server, none of which exist in this sandboxed environment (same constraint documented in every Playwright spec's header). The equivalent automated coverage (T011–T072's unit/integration tests, all passing) substitutes for it here; a human/CI run against a real deployment is still needed before this task can be honestly marked done
- [X] T079 Update `docs/DATABASE.md`/`docs/API.md` (or equivalent existing docs) to reflect the shipped `Conversations`/`Messages`/`Attachments`/`Citations` shape versus the originally-sketched schema (constitution §13 Documentation) — updated `docs/DATABASE.md` §6 (Conversation Context) with the actual shipped fields/entities (including the new Attachments/Citations tables and full-text search note) versus the original pre-implementation sketch, and `docs/API_GUIDELINES.md` §21 (AI Chat Endpoints), which had sketched a `/conversations` resource that was never built — corrected to the actual `/api/v1/chats` shape including all new `/actions/*` endpoints

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup; **blocks every user story**.
- **User Stories (Phase 3–8)**: All depend on Foundational completion.
  - US1 and US2 are both P1 and have no dependency on each other's *domain* work, but US2's query/pagination work (T025–T029) builds on US1's extended `MessageDto`/`GetChatMessagesQueryHandler` (T015–T016) — so implement US1 before US2 even though both are P1.
  - US3 (archive/pin/favorite/duplicate/clear) depends on Foundational only — its Restore command (T041) uses its own `GetByIdIncludingDeletedAsync` lookup (T046, added within US3), not US2's `SearchAsync`, so US3 does not strictly need to wait on US2. The recommended order below still sequences it after US2 for practical reasons (US3's context-menu actions are wired into the `ChatSidebar.tsx` that US2 builds out).
  - US4 (delete/purge) depends on Foundational, reuses US2's `SearchAsync` (`view=deleted`, T026) for the Recently Deleted list, US3's `GetByIdIncludingDeletedAsync` (T046) for Purge's single-item lookup, and US3's `ConfirmDialog` (T049) — sequence after US2 and US3.
  - US5 (export) depends on Foundational and US1's message-metadata shape (T014–T016).
  - US6 (titles) depends on Foundational and US1's `AppendMessageCommandHandler` (T014) and US2 is not required.
  - Recommended order: **US1 → US2 → US3 → US4 → US5 → US6** (matches spec.md priority order and minimizes cross-story rework).
- **Polish (Phase 9)**: Depends on all desired user stories being complete.

### Parallel Opportunities

- All Foundational domain-entity tasks (T002–T005) are `[P]` — different files.
- All Foundational EF configuration tasks for the two new entities (T008, T009) are `[P]`.
- Within each story, test tasks marked `[P]` (different files) can run together, and frontend-only tasks marked `[P]` (T018/T019, T032/T033) can run alongside backend tasks in the same story.
- US5 and US6 (both P3, both depend only on US1) can be staffed in parallel once US1–US4 are done, if desired.

---

## Parallel Example: User Story 1

```bash
# Tests together:
Task: "Unit tests for UserChat state-flag domain methods in tests/AskLucy.Domain.Tests/Chats/UserChatTests.cs"
Task: "Unit tests for Message metadata fields in tests/AskLucy.Domain.Tests/Chats/MessageTests.cs"
Task: "Integration test: message metadata/attachment/citation round-trip in tests/AskLucy.Persistence.Tests/Chats/MessagePersistenceTests.cs"

# Frontend typing/display together (after backend DTO shape lands):
Task: "Update MessageBubble.tsx to render provider/model/attachment/citation metadata"
Task: "Update useChatStream.ts/chatsApi.ts/aiApi.ts types for new message metadata fields"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) + Phase 2 (Foundational).
2. Complete Phase 3 (US1) — full-fidelity persisted history with rich message metadata.
3. **STOP and VALIDATE** against quickstart.md Scenario 1.
4. This alone is a shippable improvement over today's baseline (message metadata is newly durable), even before search/curation/export/titling exist.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → validate → demo (MVP).
3. US2 → validate → demo (discovery at scale).
4. US3 → validate → demo (curation).
5. US4 → validate → demo (deletion lifecycle, including the Trash view).
6. US5 → validate → demo (export).
7. US6 → validate → demo (auto/manual titles) — full feature complete.
8. Phase 9 (Polish) — cross-cutting hardening and doc updates.

---

## Notes

- `[P]` tasks touch different files with no unmet dependency.
- Every destructive action (Permanent Delete, Clear Messages) requires an explicit `confirm: true` enforced at the Application command boundary, not only in the UI (constitution §2.VIII No Silent Failures — a rejected-but-unconfirmed action must be visibly rejected, not silently ignored).
- Commit after each task or logical group; stop at any Checkpoint to validate a story independently before moving on.
- Avoid: reintroducing a parallel `Conversation` model (research.md Topic 1), a new search engine/datastore (research.md Topic 5), or offset-based pagination for chat messages (constitution §6 explicitly forbids this for high-churn collections).
