# UI Contract: Settings Navigation & Chat Configuration Hub

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md)

## Settings tab order (`SettingsPage.tsx`)

Security, Account, AI Providers, Voice, **Chat Configuration** *(new)*, **Chat History**
*(new)*, Data, Cookies. AI Providers and Voice keep their existing tab content and behavior
verbatim (research.md Decision 4).

`SettingsPage`'s tab index MUST be seedable from `location.state?.tab` (falling back to
`0`/Security), so both the Chat Configuration hub's internal links and the account/settings
menu entries (below) can land directly on a specific tab.

## Chat Configuration tab contents

A landing/hub view containing, in order:

1. **Current conversation model control** — hosted directly (not a link). Reads
   `activeConversationStore.activeChatId`; if `null`, renders a disabled/empty state
   ("No conversation is currently open"). If set, fetches `GET /api/v1/chats/{id}`
   (contracts/chat-detail-api.md) and renders a provider/model picker (reusing
   `ProviderModelSelector`'s existing UI, relocated rather than rewritten) whose `onSelect`
   calls the existing `PATCH /api/v1/chats/{id}/model-selection`.
2. **Entry point → AI Providers** — a card/link; `onClick` navigates to
   `/settings` with `location.state.tab` set to the AI Providers tab index. Label/subtext
   references "default model for new conversations."
3. **Entry point → Voice** — a card/link; same navigation mechanism, targeting the Voice
   tab index. Label/subtext references "voice, speech-to-text, and text-to-speech."

Chat Configuration MUST NOT render AI Providers' or Voice's controls inline (FR-002/FR-003).

## Chat History tab contents

Hosts the relocated `ConversationList` (from `ChatSidebar.tsx`) in full — search, filter
(All/Favorites/Pinned/Archived/Recently Deleted), sort, inline rename, pin/favorite/archive/
duplicate/export/delete. Selecting a conversation sets `activeConversationStore.activeChatId`
and navigates to `/studio`.

## Chat workspace toolbar (`ConversationView`, rendered from `ChatPage.tsx`)

Removed: `ProviderModelSelector`, the `ConversationSwitcher`/conversation-list popover.

Added: a "New chat" action directly in the toolbar (previously only reachable via the
now-relocated conversation list), so starting a new conversation stays an in-workspace,
everyday action (FR-009).

Unchanged: `VoiceControlBar`, `ChatComposer` (including its mic button), `LanguageSelector`,
`InsertPromptPicker`, `ProjectPicker` (memory project assignment).

## Account/settings entry points (two menus, kept in sync per existing convention)

- `src/components/UserMenu.tsx` — add two `MenuItem`s: "Chat Configuration" (navigates to
  `/settings` with the Chat Configuration tab index) and "Chat History" (same, with the
  Chat History tab index), positioned adjacent to the existing "Settings" item.
- `useAccountControl()` in `src/features/chat/workspaceControls.tsx` — mirror the same two
  entries, same navigation targets, following the file's existing explicit
  keep-these-two-in-sync comment.
