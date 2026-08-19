---

description: "Task list for Chat Configuration in User Settings (025-chat-configuration-settings)"

---

# Tasks: Chat Configuration in User Settings

**Input**: Design documents from `specs/025-chat-configuration-settings/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/chat-detail-api.md, contracts/settings-navigation.md, quickstart.md

**Tests**: Included. Constitution §10 requires tests for new/changed behavior in the same PR, and this codebase's existing convention pairs every component with a `*.test.tsx` (and, where interactive, a `*.a11y.test.tsx` via jest-axe) and every Application handler with an xUnit test — this feature follows that convention throughout.

**Organization**: Tasks are grouped by user story (spec.md) to enable independent implementation and testing of each story.

## Path Conventions

Full-stack feature. Frontend paths are relative to `src/AskLucy.Web/ClientApp/` unless stated otherwise. Backend paths are relative to the repository root (`src/AskLucy.Domain`, `src/AskLucy.Application`, `src/AskLucy.Web`, `tests/AskLucy.*.Tests`).

---

## Phase 1: Setup

**Purpose**: A single shared source of truth for Settings tab indices, so every later phase (SettingsPage itself, Chat Configuration's entry-point links, and both account menus) references the same constants instead of magic numbers.

- [X] T001 [P] Create `src/features/settings/settingsTabs.ts` exporting a `SETTINGS_TAB_INDEX` const map (`Security: 0, Account: 1, AiProviders: 2, Voice: 3, ChatConfiguration: 4, ChatHistory: 5, Data: 6, Cookies: 7`) per research.md Decision 4

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared active-conversation tracking and the two new Settings tab slots every user story phase builds on.

**⚠️ CRITICAL**: No user story work in Phase 3 onward can begin until this phase is complete.

- [X] T002 [P] Implement `activeConversationStore` (Zustand, `sessionStorage`-persisted) in `src/features/chat/activeConversationStore.ts` — `activeChatId: string | null`, `setActiveChatId(id: string | null)` per data-model.md
- [X] T003 [P] Unit tests for `activeConversationStore` in `src/features/chat/activeConversationStore.test.ts` (setting/clearing persists across a re-created store instance within the same session; a fresh session with no prior state defaults to `null`)
- [X] T004 Wire `src/features/chat/pages/ChatPage.tsx` so every place `selectedChatId` changes (opening, switching to, or creating a conversation) also calls `activeConversationStore`'s `setActiveChatId` (depends on T002) — purely additive; no visible/behavioral change yet
- [X] T005 Extend `src/features/settings/pages/SettingsPage.tsx`'s `Tabs`/`TabPanel` list with two new tab slots — "Chat Configuration" and "Chat History" (empty placeholders for now) — positioned after "Voice" and before "Data", using the `SETTINGS_TAB_INDEX` constants from T001; seed the initial `tab` state from `location.state?.tab` (falling back to `0`) per research.md Decision 4 (depends on T001)
- [X] T006 [P] Update `src/features/settings/pages/SettingsPage.a11y.test.tsx` and add/extend a `SettingsPage.test.tsx` confirming all 8 tabs render (including the two new placeholders) and that `location.state.tab` correctly seeds the initially active tab

**Checkpoint**: Shared active-conversation tracking exists and is wired into the workspace; Settings has two ready-to-fill, correctly-indexed tab slots. User story phases can now begin.

---

## Phase 3: User Story 1 - A single hub for AI model and voice configuration (Priority: P1) 🎯 MVP

**Goal**: A "Chat Configuration" hub tab in Settings hosts a control for changing the model of the conversation the user currently has open, plus entry-point links into the existing, unchanged "AI Providers" and "Voice" tabs.

**Independent Test**: Open Settings → Chat Configuration without opening the chat workspace at all; confirm the current-conversation control and both entry-point links render and work; open a conversation in `/studio`, return to Chat Configuration, confirm it reflects and can change that conversation's model; follow both entry-point links and confirm they land on the unmodified AI Providers/Voice tabs.

### Backend for User Story 1

- [X] T007 [P] [US1] Implement `GetChatByIdQuery`, `GetChatByIdQueryHandler`, and `ChatDetailDto` (`Id, Title, ProviderId, ModelId`) in `src/AskLucy.Application/Chats/Queries/GetChatById/`, enforcing ownership via the existing `ChatOwnershipGuard` pattern (research.md Decision 2, contracts/chat-detail-api.md)
- [X] T008 [P] [US1] Unit tests for `GetChatByIdQueryHandler` in `tests/AskLucy.Application.Tests/Chats/GetChatByIdQueryHandlerTests.cs` (NSubstitute-faked repository) — returns the caller's own chat detail, including null `ProviderId`/`ModelId` for a chat with no selection persisted yet; denies/not-found for a chat owned by a different user
- [X] T009 [US1] Add `[HttpGet("{id:guid}")]` action to `src/AskLucy.Web/Controllers/v1/ChatsController.cs` returning `ActionResult<ChatDetailDto>` (depends on T007)
- [X] T010 [P] [US1] Extend `tests/AskLucy.Web.Tests/Chats/OwnershipTests.cs` with a `GetChatById_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent` case, and update its doc comment (which currently states "there is no single-chat GET endpoint") to reflect the new route (depends on T009)

### Frontend for User Story 1

- [X] T011 [P] [US1] Add `getChatById(id)` and a `ChatDetail` interface (`id, title, providerId, modelId`) to `src/features/chat/api/chatsApi.ts` per contracts/chat-detail-api.md
- [X] T012 [US1] Create `src/features/settings/pages/ChatConfigurationTab.tsx` — hosts the current-conversation model control (reusing the existing `ProviderModelSelector` component, fed by `getChatById`/`activeConversationStore.activeChatId` on read and `updateChatModelSelection` on write) plus two entry-point links to AI Providers and Voice via `navigate('/settings', { state: { tab: SETTINGS_TAB_INDEX.AiProviders } })` (and the Voice equivalent); renders a "no conversation is currently open" state when `activeChatId` is `null`, and — distinct from that state — renders the same "no AI providers configured" empty-state message `AiProvidersTab` already uses (not `ProviderModelSelector`'s current blank/`null` render) when the active-provider catalog is empty, per spec.md Edge Cases (depends on T001, T002, T011)
- [X] T013 [US1] Render `ChatConfigurationTab` into the Chat Configuration `TabPanel` slot added in T005, in `src/features/settings/pages/SettingsPage.tsx` (depends on T005, T012)
- [X] T014 [P] [US1] Tests for `ChatConfigurationTab` in `src/features/settings/pages/ChatConfigurationTab.test.tsx` (MSW-mocked API, per `AiProvidersTab.test.tsx`'s convention) — "no conversation open" empty state when `activeChatId` is null; "no AI providers configured" empty-state message when the active-provider catalog is empty (FR-005, spec.md Edge Cases); the current-conversation model picker only ever lists admin-active providers/models (FR-005); fetches and displays the open conversation's current provider/model when set; changing the model calls `updateChatModelSelection` and reflects immediately; both entry-point links navigate with the correct `location.state.tab`; neither `AiProvidersTab`'s nor `VoiceTab`'s own controls render inline inside `ChatConfigurationTab` (FR-012); a failed fetch or save surfaces visible UI feedback, never a console-only failure (constitution §2.VIII)
- [X] T015 [P] [US1] Accessibility test for `ChatConfigurationTab` in `src/features/settings/pages/ChatConfigurationTab.a11y.test.tsx` (jest-axe, zero violations in both the "no conversation open" and "conversation open" states)

**Checkpoint**: Chat Configuration hub is fully functional and independently testable/demoable from Settings — the chat workspace toolbar is untouched so far.

---

## Phase 4: User Story 2 - Browse and reopen chat history from Settings (Priority: P2)

**Goal**: A standalone "Chat History" Settings tab, independent of Chat Configuration, hosts the full existing conversation-list capability.

**Independent Test**: Open Settings → Chat History (not nested under Chat Configuration); search/filter/sort/pin/favorite/archive/duplicate/export/delete a conversation and confirm each behaves exactly as the prior in-workspace list; select a conversation and confirm the workspace opens with it active.

- [X] T016 [US2] Create `src/features/settings/pages/ChatHistoryTab.tsx`, relocating `ConversationList`'s usage (from `src/features/chat/components/ChatSidebar.tsx`) unchanged — search/filter/sort/rename/pin/favorite/archive/duplicate/export/delete; selecting a conversation calls `activeConversationStore.setActiveChatId(id)` and navigates to `/studio` (depends on T002, T005)
- [X] T017 [US2] Render `ChatHistoryTab` into the Chat History `TabPanel` slot added in T005, in `src/features/settings/pages/SettingsPage.tsx` (depends on T005, T016)
- [X] T018 [P] [US2] Tests for `ChatHistoryTab` in `src/features/settings/pages/ChatHistoryTab.test.tsx` — search/filter/sort/pin/favorite/archive/duplicate/export/delete all behave identically to the prior in-workspace `ConversationList` coverage; selecting a conversation sets `activeChatId` and navigates to `/studio`; "no conversations yet" empty state with zero conversations
- [X] T019 [P] [US2] Accessibility test for `ChatHistoryTab` in `src/features/settings/pages/ChatHistoryTab.a11y.test.tsx` (jest-axe, zero violations)

**Checkpoint**: Chat History reachable and fully functional from Settings, independent of Chat Configuration. On top of US1 (shares only the Foundational phase).

---

## Phase 5: User Story 3 - A visually clean chat workspace (Priority: P2)

**Goal**: The chat toolbar loses the live provider/model switcher and the conversation-history panel — safe now that US1 and US2 provide equivalent capability elsewhere — while gaining a direct "New chat" action so starting a conversation stays workspace-native.

**Independent Test**: Open `/studio`; confirm the toolbar no longer shows a provider/model switcher or a "Conversations" button; confirm sending a message, muting/unmuting voice, toggling conversation mode, and starting a new conversation all still work directly in the workspace.

- [X] T020 [US3] Add a "New chat" action directly to `ConversationView`'s toolbar (rendered from `src/features/chat/pages/ChatPage.tsx`), independent of the conversation list, per FR-009 (depends on T004)
- [X] T021 [US3] Remove the `ProviderModelSelector` and `ConversationSwitcher` usages from `ConversationView`'s toolbar in `src/features/chat/pages/ChatPage.tsx` — `ProviderModelSelector` the component stays (now used only by `ChatConfigurationTab`, from T012); only its toolbar usage here is removed (depends on T012, T016, T020 — the equivalent capability must exist in Settings before removal)
- [X] T022 [P] [US3] Delete the now-fully-unused `src/features/chat/components/ConversationSwitcher.tsx` and `ConversationSwitcher.test.tsx` (depends on T021)
- [X] T023 [P] [US3] Update `src/features/chat/pages/ChatPage.test.tsx` and `ChatPage.a11y.test.tsx`: toolbar no longer renders a provider/model switcher or conversation-history panel; "New chat" is directly clickable and starts a new conversation; sending a message, muting/unmuting, toggling conversation mode, activating the microphone, inserting a saved prompt, selecting a translation language, and assigning a conversation to a memory project all remain unchanged (FR-009 — full list)

**Checkpoint**: Workspace toolbar is clean; equivalent capability is fully preserved via Settings (US1 + US2); zero regression in everyday chat actions.

---

## Phase 6: User Story 4 - Reach both destinations without leaving the flow (Priority: P3)

**Goal**: Both account/settings menus (the standalone `UserMenu` and the in-studio `useAccountControl()`) offer "Chat Configuration" and "Chat History" entries, landing on the correct tab from any path.

**Independent Test**: From `/studio`, open the account/settings menu and confirm both entries are present and route correctly; separately, reach Settings via any other path and confirm the same two sections with identical behavior (not a second, differently-scoped copy).

- [X] T024 [P] [US4] Add "Chat Configuration" and "Chat History" `MenuItem`s to `src/components/UserMenu.tsx`, each navigating to `/settings` with the corresponding `SETTINGS_TAB_INDEX` via `location.state.tab` (depends on T001, T005)
- [X] T025 [P] [US4] Mirror the same two entries in `useAccountControl()` in `src/features/chat/workspaceControls.tsx`, per its existing explicit keep-these-two-menus-in-sync convention (depends on T001, T005)
- [X] T026 [P] [US4] Update `src/components/UserMenu.test.tsx` (or the appropriate existing menu test) confirming both new entries navigate to the correct Settings tab
- [X] T027 [P] [US4] Update `src/features/chat/pages/ChatPage.test.tsx` confirming both entries are reachable from the workspace's account control in two clicks or fewer and navigate correctly

**Checkpoint**: All four user stories independently functional; entry points into Settings are consistent from every path.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation across all four stories together.

- [X] T028 Run all `quickstart.md` scenarios end-to-end (Chat Configuration hub, Chat History, clean workspace, consistent entry points, regression checks) against a locally running instance — verified live via a real registered/confirmed account against the dev backend; found and fixed a real bug in the process (see Notes)
- [X] T029 [P] Extend `src/features/settings/pages/SettingsPage.a11y.test.tsx` with a full-page jest-axe pass across all 8 tabs, post-integration
- [X] T030 Sweep `src/features/chat/pages/ChatPage.tsx` and related files for stale doc comments referencing the removed in-workspace provider/model switcher or conversation-history panel, updating or removing them

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup (T001) completion — BLOCKS all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational (Phase 2). No dependency on US2/US3/US4.
- **User Story 2 (Phase 4)**: Depends on Foundational (Phase 2) only. Independent of US1 — can run in parallel with Phase 3.
- **User Story 3 (Phase 5)**: Depends on Foundational (Phase 2) **and** on US1 (T012) and US2 (T016) being complete — removing the in-workspace controls is only safe once their Settings-side replacements exist (spec.md User Story 3's own stated precondition).
- **User Story 4 (Phase 6)**: Depends on Foundational (Phase 2) and on the tab slots existing (T005); does not require US1/US2/US3's internal implementation to be finished, only that the tab indices they target are valid destinations — can proceed in parallel with US1–US3 once Phase 2 is done, though verifying it end-to-end benefits from US1/US2 being in place.
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### Within Each User Story

- Backend before the frontend code that calls it (US1: T007–T010 before T011–T015).
- Tests for a component are written alongside/after that component's implementation task, per this codebase's existing convention (not strict TDD).
- Story complete and checkpointed before the next-dependent story (US3) begins removal work.

### Parallel Opportunities

- T001 (Setup) has no dependencies and can start immediately.
- T002/T003 (store + its test) and T005/T006 (SettingsPage tabs) touch different files and can proceed in parallel once T001 is done.
- Once Phase 2 is complete, US1 (Phase 3) and US2 (Phase 4) can be implemented in parallel — they touch different new files (`ChatConfigurationTab.tsx` vs. `ChatHistoryTab.tsx`) and different backend areas (US1 adds a backend query; US2 adds none).
- US4 (Phase 6)'s tasks are all `[P]` against each other and can start as soon as Phase 2's tab slots exist.
- US3 (Phase 5) is the one story with a real cross-story dependency (on US1 + US2) — do not parallelize its removal tasks ahead of them.

---

## Parallel Example: User Story 1

```bash
# Backend, once Phase 2 is complete:
Task: "Implement GetChatByIdQuery/Handler/ChatDetailDto in src/AskLucy.Application/Chats/Queries/GetChatById/"
Task: "Unit tests for GetChatByIdQueryHandler in tests/AskLucy.Application.Tests/Chats/GetChatByIdQueryHandlerTests.cs"

# Frontend, once T011 (API client) and T012 (component) land:
Task: "Tests for ChatConfigurationTab in src/features/settings/pages/ChatConfigurationTab.test.tsx"
Task: "Accessibility test for ChatConfigurationTab in src/features/settings/pages/ChatConfigurationTab.a11y.test.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories).
3. Complete Phase 3: User Story 1 — Chat Configuration hub, including the new backend endpoint.
4. **STOP and VALIDATE**: Run quickstart.md Scenario 1 independently. The chat workspace toolbar is still untouched at this point, so there is zero regression risk even mid-rollout.
5. Deploy/demo if ready.

### Incremental Delivery

1. Setup + Foundational → foundation ready, no visible change yet.
2. Add User Story 1 → Chat Configuration hub live in Settings → test independently → demo (MVP!).
3. Add User Story 2 → Chat History live in Settings → test independently → demo.
4. Add User Story 3 → workspace toolbar cleaned up, now that both replacements exist → test independently → demo.
5. Add User Story 4 → both account menus updated → test independently → demo.
6. Polish → full quickstart.md pass, full-page a11y sweep, stale-comment cleanup.

### Parallel Team Strategy

With multiple developers, once Phase 2 (Foundational) is done:

- Developer A: User Story 1 (Phase 3) — includes the one backend task.
- Developer B: User Story 2 (Phase 4).
- Developer C: User Story 4 (Phase 6) — only needs the Phase 2 tab slots to exist.
- User Story 3 (Phase 5) is picked up by whoever finishes first, once both US1 and US2 are checkpointed complete.

---

## Notes

- `[P]` tasks touch different files with no blocking dependency on another incomplete task in the same batch.
- `[Story]` label maps each task to its user story for traceability.
- User Story 3 is the one deliberate exception to "stories are independent" — its removal work is only safe after US1 and US2 exist, matching spec.md's own stated rationale for its priority/ordering.
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently before continuing.

## Post-Implementation Note (T028 live verification)

Live browser verification against the dev backend caught one real bug the automated test
suite missed: `SettingsPage.tsx`'s `tab` state was seeded via `useState`'s lazy initializer,
which only runs on first mount. Chat Configuration's own "Go to AI Providers"/"Go to Voice"
links call `navigate('/settings', { state: { tab } })` while `/settings` is already the
current route, so `SettingsPage` never remounts and the tab silently failed to switch —
every automated test happened to render a *fresh* `SettingsPage` per assertion, which is why
this didn't surface until a real click-through in a real browser. Fixed by re-syncing `tab`
off `location.key` (changes on every `navigate()` call, including same-pathname ones) in a
`useEffect`; a regression test was added to `SettingsPage.test.tsx` covering the
already-mounted case directly. Also discovered: this dev environment's remote database has no
AI providers configured, which correctly exercises (and confirms) every "no AI providers
enabled yet" empty-state path across Chat Configuration, but means the "happy path with a
real provider" (selecting/switching an actual model) could not be exercised live — that
remains covered only by the mocked automated tests.
