# Research: Chat Configuration in User Settings

**Feature**: [../spec.md](spec.md)

All decisions below resolve the "how" behind decisions already locked in the spec's
Clarifications session (2026-08-17). No open `NEEDS CLARIFICATION` markers remain in
Technical Context after this document.

## Decision 1 — How does a page outside the chat workspace know which conversation is "currently open"?

**Decision**: Introduce a small persisted Zustand store, `activeConversationStore`
(`src/features/chat/activeConversationStore.ts`), holding `activeChatId: string | null`,
persisted to `sessionStorage` (survives navigation within the tab/session, cleared on tab
close — consistent with a "currently open" concept rather than a permanent preference).
`ChatPage` sets it whenever `selectedChatId` changes (including the id becoming `null` when
the user is on a brand-new, not-yet-persisted chat) instead of, or in addition to, its local
`useState`. Chat Configuration reads `activeChatId` from the same store to know whether to
render the current-conversation model control or the "no conversation open" empty state
(spec Edge Cases).

**Rationale**: Confirmed via code inspection that `selectedChatId` in `ChatPage.tsx` is
plain component `useState` with no URL param (`/studio` carries no chat id in its route) and
no shared store today — it is lost the moment `ChatPage` unmounts, which is exactly what
happens when a user navigates to `/settings`. FR-004 requires Chat Configuration to control
the model of "the conversation the user currently has open," so that identity must survive
the navigation. A small dedicated store is the minimal change consistent with the existing
architecture (Zustand is already the project's client-state tool per constitution §7) and
does not require restructuring `ChatPage`'s existing chat-switching logic.

**Alternatives considered**:
- *Route param* (`/studio/:chatId`): more invasive (touches routing, deep-linking,
  every internal navigation call) for a need that's satisfied by a much smaller store; also
  doesn't inherently solve the "read it from Settings" problem without also persisting it
  somewhere Settings can reach — the persisted store is required either way.
- *`localStorage` instead of `sessionStorage`*: rejected — a conversation "currently open"
  should not silently reassert itself as active in a brand-new browser session days later;
  `sessionStorage` matches the concept's actual lifetime.
- *No persistence, pass chatId only via a query param on the Settings navigation link*:
  rejected — fails User Story 4's requirement that reaching Chat Configuration through any
  path (not just the in-workspace shortcut) shows the same, correct state.

## Decision 2 — How does Chat Configuration read and write the current conversation's provider/model?

**Decision**: Add one new minimal read query/endpoint, `GET /api/v1/chats/{id}` →
`ChatDetailDto { Id, Title, ProviderId, ModelId }`, backed by a new
`GetChatByIdQuery`/`GetChatByIdQueryHandler` in
`src/AskLucy.Application/Chats/Queries/GetChatById/`, following the existing per-query
folder convention (`ExportUserChat/`, `GetChatMessages/`, `SearchUserChats/`). Writing
continues to use the existing `PATCH /api/v1/chats/{id}/model-selection`
(`UpdateChatModelSelectionCommand`) verbatim — no backend change needed there.

**Rationale**: Confirmed via code inspection that `UserChat.ProviderId`/`ModelId` (nullable
`Guid` FKs) are already persisted columns, already written by
`UpdateChatModelSelectionCommandHandler` — the data exists — but no query or DTO anywhere
projects them back out. `ChatsController` has no `GET /api/v1/chats/{id}` route at all today
(only list, messages, memory-references, export). Chat Configuration, rendered on a
different page than the chat workspace, cannot rely on in-memory React state to prefill the
control; it must fetch the value. This is the smallest addition that fills the gap: one
query, one handler, one controller action, reusing the existing `ChatOwnershipGuard`
authorization pattern from `UpdateChatModelSelectionCommandHandler` so the new endpoint is
scoped to the caller's own chat exactly like every other chat endpoint (constitution §8).
The DTO carries only `ProviderId`/`ModelId` (raw ids) — display names are resolved
client-side via the same `useAiProviders`/`useAiModels` catalog hooks `AiProvidersTab` and
the removed `ProviderModelSelector` already use, avoiding a duplicate server-side join
(CQRS rule: queries return only what the caller needs, constitution §3).

**Alternatives considered**:
- *Extend `UserChatSummaryDto`/`SearchUserChatsQuery` to include `ProviderId`/`ModelId`*:
  rejected as the primary mechanism — Chat Configuration needs one specific chat's detail,
  not a page of summaries; fetching a full list to read one id's fields would be wasteful
  and is not how any other single-resource read in this controller works.
- *No backend change; have Chat Configuration silently show blank/unselected until the user
  picks a model*: rejected — violates FR-004 ("preserve... live, mid-conversation
  model-switching capability") and the constitution's no-silent-failure principle (§2.VIII)
  by presenting an incorrect/misleading initial state rather than the real one.

## Decision 3 — Where do the relocated conversation-list actions live, and do they need backend changes?

**Decision**: None. `ConversationList` (currently exported from
`src/features/chat/components/ChatSidebar.tsx`) and its existing search/filter/sort/pin/
favorite/archive/duplicate/export/delete wiring are relocated as-is into a new
`ChatHistoryTab` rendered from `SettingsPage.tsx`, reusing `searchChats`, `pinChat`,
`favoriteChat`, `archiveChat`, `duplicateChat`, `exportChat`, `deleteChat`/`purgeChat`,
`restoreChat` exactly as they exist in `chatsApi.ts` today. Selecting a conversation writes
`activeChatId` (Decision 1) and navigates to `/studio`.

**Rationale**: FR-006/FR-007 require exact preservation of existing behavior with zero new
data concepts (spec Assumptions: "a relocation of the existing conversation list's UI, not a
new data feature"). All backing endpoints already exist and are already fully functional;
this is a presentation-layer move.

**Alternatives considered**: A dedicated `/api/v1/chats/history` endpoint mirroring
`searchChats` — rejected as unnecessary duplication of `SearchUserChatsQuery`, which already
serves this exact need.

## Decision 4 — How do "AI Providers" and "Voice" stay unchanged while being linked from Chat Configuration?

**Decision**: `SettingsPage.tsx`'s existing `Tabs`/`tab: number` local-state pattern is
extended with two additional tabs — "Chat Configuration" and "Chat History" — inserted
after "Voice" and before "Data" (order: Security, Account, AI Providers, Voice, **Chat
Configuration**, **Chat History**, Data, Cookies). `AiProvidersTab` and `VoiceTab` are not
modified. Chat Configuration's "entry point" links use `useNavigate` with React Router
`location.state` to set the target tab index (`navigate('/settings', { state: { tab:
AI_PROVIDERS_TAB_INDEX } })`), and `SettingsPage` seeds its initial `tab` state from
`location.state?.tab` when present (falling back to `0`). The same mechanism is reused by
the account/settings menu entries (Decision 5) to land directly on Chat Configuration or
Chat History.

**Rationale**: Settings navigation today is pure component state, not routed per-tab
(`/settings` has one route, no sub-paths or hash). Introducing tab-scoped sub-routes would
be a larger structural change than this feature needs (constitution §7, Convention over
Configuration — reuse the existing pattern rather than a parallel routing mechanism) and
none of the acceptance criteria require a bookmarkable per-tab URL. `location.state` is
already an established React Router mechanism and keeps the change confined to
`SettingsPage.tsx`'s existing tab-index model.

**Alternatives considered**: Route-per-tab (`/settings/ai-providers`, etc.) — rejected as
disproportionate scope for a presentation reorganization; would also touch every existing
test that currently drives `SettingsPage` by clicking `Tab` elements.

## Decision 5 — Account/settings menu integration (two parallel menus)

**Decision**: Add two new destinations — "Chat Configuration" and "Chat History" — to both
existing menu implementations: `src/components/UserMenu.tsx` (used by `AppShell`, i.e. the
standalone Settings page and most non-studio pages) and `useAccountControl()` in
`src/features/chat/workspaceControls.tsx` (used inside the Flumeria Studio shell). Both
already navigate to `/settings`; the new entries pass `location.state.tab` per Decision 4.

**Rationale**: This is an existing, already-flagged manual-sync point (a code comment in
`workspaceControls.tsx` already warns the two lists must be kept in sync) — not a new
architectural decision this feature introduces, just an existing convention this feature
must follow. No unification of the two menu mechanisms is in scope; that would be a larger,
unrelated refactor.

## Decision 6 — Removing `ProviderModelSelector` and the in-workspace conversation panel from the toolbar

**Decision**: `ProviderModelSelector` and `ConversationSwitcher`/`ChatSidebar`'s
`ConversationList` usage are removed from `ConversationView`'s toolbar (rendered inside
`ChatPage.tsx`). `ChatPage` gains a "New chat" affordance directly in the toolbar (replacing
the one that previously lived inside the now-relocated conversation list's empty state /
sidebar header), since FR-009 requires starting a new conversation to remain an everyday,
in-workspace action independent of history browsing.

**Rationale**: Directly implements FR-008/FR-009. The existing "New chat" button
(`ChatSidebar.tsx`) is currently only reachable via the conversation list being removed from
the workspace; a standalone entry point must be added so starting a new conversation doesn't
regress into requiring a trip to Settings.

**Alternatives considered**: Leaving "New chat" inside the relocated Chat History Settings
tab only — rejected, contradicts FR-009 and spec Edge Cases ("they MUST be able to do so
directly from the workspace without navigating to Settings").

## Decision 7 — Accessibility and testing approach

**Decision**: Follow the existing pattern already established for `SettingsPage.tsx`
(`SettingsPage.a11y.test.tsx` using `jest-axe`) — the new Chat Configuration and Chat
History tabs get equivalent automated a11y assertions, plus Vitest/Testing Library
interaction tests following `AiProvidersTab.test.tsx`/`VoiceTab.test.tsx`'s existing
conventions (MSW-mocked API calls). No new tooling introduced.

**Rationale**: Constitution §7/§10 requires WCAG 2.1 AA + automated a11y checks for new UI;
the project already has the tooling and pattern in place for this exact page.
