import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface ChatPanelSizeState {
  isFullHeight: boolean
  toggle: () => void
}

/**
 * specs/030-composer-panel-refinements FR-008a, research.md Decision 4 — the user's
 * last-chosen `ExpandedChatPanel` height (half-height default vs. full window height),
 * persisted to `localStorage` so it survives a reload. A lightweight, per-device UI
 * preference mirroring `src/store/themeStore.ts`'s shape — no backend sync, unlike
 * `panelPreferencesStore.ts`'s cross-device viewer opacity setting (data-model.md).
 */
export const useChatPanelSizeStore = create<ChatPanelSizeState>()(
  persist(
    (set, get) => ({
      isFullHeight: false,
      toggle: () => set({ isFullHeight: !get().isFullHeight }),
    }),
    { name: 'ask-lucy-chat-panel-size' },
  ),
)
