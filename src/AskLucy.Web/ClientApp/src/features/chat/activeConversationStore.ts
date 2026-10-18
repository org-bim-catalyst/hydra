import { create } from 'zustand'
import { createJSONStorage, persist } from 'zustand/middleware'
import { useAuthStore } from '../../store/authStore'

interface ActiveConversationState {
  /** The conversation open right now, for this page load. */
  activeChatId: string | null
  /** The last conversation each account had open, by user id. Only ever read for the account that wrote it. */
  lastChatByUser: Record<string, string>
  setActiveChatId: (id: string | null) => void
  /** The conversation to open for the signed-in account: the one open now, else its last one. */
  resumeChatId: () => string | null
}

const signedInUserId = () => useAuthStore.getState().userId

/**
 * specs/025-chat-configuration-settings research.md Decision 1 — tracks which conversation the user has open,
 * so Chat Configuration in Settings knows whether there is a current conversation to act on.
 *
 * The open conversation is in memory. The last one each account had open is kept per user id in `localStorage`,
 * so a reload or a later sign-in as the same account reopens it, while another account never inherits it.
 */
export const useActiveConversationStore = create<ActiveConversationState>()(
  persist(
    (set, get) => ({
      activeChatId: null,
      lastChatByUser: {},
      setActiveChatId: (id) => {
        const userId = signedInUserId()
        const lastChatByUser = id && userId ? { ...get().lastChatByUser, [userId]: id } : get().lastChatByUser
        set({ activeChatId: id, lastChatByUser })
      },
      resumeChatId: () => {
        const userId = signedInUserId()
        return get().activeChatId ?? (userId ? (get().lastChatByUser[userId] ?? null) : null)
      },
    }),
    {
      name: 'ask-lucy-last-conversation',
      storage: createJSONStorage(() => localStorage),
      partialize: (state) => ({ lastChatByUser: state.lastChatByUser }),
    },
  ),
)
