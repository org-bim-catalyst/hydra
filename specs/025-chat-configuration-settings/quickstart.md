# Quickstart: Validating Chat Configuration in User Settings

**Feature**: [spec.md](spec.md) | **Data model**: [data-model.md](data-model.md) | **Contracts**: [contracts/](contracts/)

## Prerequisites

- Backend (`src/AskLucy.Web`) and frontend (`src/AskLucy.Web/ClientApp`) running locally per
  each project's README.
- A signed-in test user with at least one AI provider/model marked active in the platform's
  provider/model catalog (Admin panel), and at least one existing conversation with messages.

## Scenario 1 — Chat Configuration hub (User Story 1)

1. Sign in, open the account/settings menu, select **Chat Configuration**.
2. Confirm the Settings page opens with the Chat Configuration tab active, alongside
   Security/Account/AI Providers/Voice/Chat History/Data/Cookies (none removed).
3. With no conversation open this session, confirm the current-conversation model control
   shows a "no conversation currently open" state, not an error.
4. Open a conversation in the workspace (`/studio`), return to Chat Configuration: confirm
   the control now shows that conversation's current provider/model.
5. Change the model via the control; confirm the change applies immediately (return to
   `/studio` and confirm the next message uses the new model — mirrors prior
   `ProviderModelSelector` behavior).
6. Click the "AI Providers" entry point; confirm it lands on the unchanged AI Providers tab.
   Click the "Voice" entry point; confirm it lands on the unchanged Voice tab. Confirm
   neither tab's controls are duplicated inside Chat Configuration itself.

## Scenario 2 — Chat History from Settings (User Story 2)

1. Open the account/settings menu, select **Chat History**.
2. Confirm it is a separate tab from Chat Configuration (not nested under it).
3. Search, filter (All/Favorites/Pinned/Archived/Recently Deleted), sort, pin, favorite,
   archive, duplicate, export, and delete a conversation — confirm each behaves exactly as
   the previous in-workspace conversation list did.
4. Select a conversation; confirm the workspace opens with that conversation active.
5. With zero conversations (a fresh test account), confirm the existing "no conversations
   yet" empty state appears.

## Scenario 3 — Clean workspace (User Story 3)

1. Open the Flumeria Studio workspace (`/studio`).
2. Confirm the toolbar no longer shows a provider/model switcher or a conversation-
   history/"Conversations" button.
3. Confirm sending a message, muting/unmuting voice, toggling push-to-talk vs. continuous
   mode, starting a new conversation, activating the microphone, inserting a saved prompt,
   selecting a translation language, and assigning a conversation to a memory project all
   still work directly in the workspace (FR-009 — full list).
4. From the workspace, reach Chat Configuration or Chat History in two clicks or fewer via
   the account/settings menu.

## Scenario 4 — Consistent entry points (User Story 4)

1. From inside the workspace, open the account menu; confirm both "Chat Configuration" and
   "Chat History" entries are present and route correctly.
2. Navigate to Settings via any other path (e.g., directly to `/settings`); confirm the same
   two sections, with the same content and behavior, are present — not a second, differently
   -scoped copy.

## Regression checks (no functionality lost)

- Voice conversation mode, mute, selected voice, speed, style, and microphone/speaker device
  selection still save correctly from the (unchanged) Voice tab.
- The default AI provider/model for new conversations still saves correctly from the
  (unchanged) AI Providers tab and still only affects conversations started after the save.
- `GET /api/v1/chats/{id}` (new) returns `404` for a chat the signed-in user does not own.
- No console errors/unhandled promise rejections during any of the above (constitution §2.VIII
  — no silent failures); automated a11y checks (`jest-axe`) pass for both new tabs.
