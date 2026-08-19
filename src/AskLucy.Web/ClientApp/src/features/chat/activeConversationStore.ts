import { create } from 'zustand'
import { createJSONStorage, persist } from 'zustand/middleware'

interface ActiveConversationState {
  activeChatId: string | null
  setActiveChatId: (id: string | null) => void
}

/**
 * specs/025-chat-configuration-settings research.md Decision 1 — tracks which conversation
 * the user currently has open so a page outside the chat workspace (Chat Configuration in
 * Settings) can know whether there's a "current conversation" to act on. `sessionStorage`
 * (not `localStorage`, unlike `voicePreferencesStore`) because "currently open" is a
 * session-lifetime concept, not a durable preference — it should not silently reassert
 * itself as active in a brand-new browser session days later.
 */
export const useActiveConversationStore = create<ActiveConversationState>()(
  persist(
    (set) => ({
      activeChatId: null,
      setActiveChatId: (id) => set({ activeChatId: id }),
    }),
    {
      name: 'ask-lucy-active-conversation',
      storage: createJSONStorage(() => sessionStorage),
    },
  ),
)
