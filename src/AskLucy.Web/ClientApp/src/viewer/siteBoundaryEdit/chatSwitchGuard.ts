import { create } from 'zustand'
import { useSiteBoundaryEditStore } from './siteBoundaryEditStore'

/** A chat switch held back because the outline editor has unsaved changes (FR-029). */
interface ChatSwitchGuardState {
  pending: { proceed: () => void } | null
  hold(proceed: () => void): void
  release(): void
}

export const useChatSwitchGuardStore = create<ChatSwitchGuardState>((set) => ({
  pending: null,
  hold: (proceed) => set({ pending: { proceed } }),
  release: () => set({ pending: null }),
}))

/**
 * specs/079 (FR-029): runs `proceed` - opening another chat, or a new one (`targetChatId` null) - unless the
 * outline editor holds unsaved changes for a different chat, in which case the user is asked first (Save,
 * Discard or Stay) by `SiteBoundaryChatSwitchDialog`, and `proceed` runs only once they have chosen to leave.
 */
export function guardChatSwitch(targetChatId: string | null, proceed: () => void): void {
  const store = useSiteBoundaryEditStore.getState()
  const session = store.session
  if (session && store.isDirty() && session.chatId !== targetChatId) {
    useChatSwitchGuardStore.getState().hold(proceed)
    return
  }

  proceed()
}
