# Data Model: Composer & Panel Layout Refinements

This feature introduces no backend/database entities (spec.md Key Entities: N/A). The only new
piece of state is a client-side UI preference, documented below in place of a traditional data
model.

## `ChatPanelSizeState` (client-only, `chatPanelSizeStore.ts`)

| Field | Type | Description |
|-------|------|-------------|
| `isFullHeight` | `boolean` | Whether the expanded chat panel is currently in its full-window-height state (`true`) or its default half-height state (`false`, the initial/default value). |
| `toggle` | `() => void` | Flips `isFullHeight`. The sole write operation — no partial/patch update, matching `themeStore.ts`'s `toggle` shape (research.md Decision 4). |

**Persistence**: Backed by `zustand/middleware`'s `persist`, `localStorage` key
`ask-lucy-chat-panel-size`. No server round-trip, no API DTO, no database column — restoring the
value on load is a synchronous localStorage read (no loading/error state to model, unlike
`panelPreferencesStore`'s server-backed `hydrateFromServer`).

**Lifecycle**: Created once at module load (module-level Zustand store, same as every other store
in this codebase); read by `ExpandedChatPanel` to pick the active height; written only by the new
resize/toggle button's `onClick`. No relationship to any other entity or store — it does not
interact with `activeConversationStore`, `voicePreferencesStore`, or any chat/message data.

## Component prop changes (not new entities, documented for traceability)

**`ExpandedChatPanelProps`** gains two new required props, both owned by the caller
(`ConversationView` in `ChatPage.tsx`) reading/writing `chatPanelSizeStore`:

| Prop | Type | Purpose |
|------|------|---------|
| `isFullHeight` | `boolean` | Drives which height `sx` branch renders (research.md Decision 3). |
| `onToggleHeight` | `() => void` | Wired to the new resize/toggle button's `onClick`. |

No other component's props change. `ChatComposerProps` is unchanged — Decision 1/2's layout and
scroll-cap changes are internal to `ChatComposer.tsx`'s JSX structure, not new inputs from the
caller.
