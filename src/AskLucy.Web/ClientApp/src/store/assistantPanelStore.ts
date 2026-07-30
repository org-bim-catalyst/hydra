import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface AssistantPanelState {
  isOpen: boolean
  hasUnreadWhileCollapsed: boolean
  open: () => void
  close: () => void
  toggle: () => void
  markUnread: () => void
}

/** FR-006/FR-016: floating assistant panel open/collapsed state, and whether an
 * assistant reply arrived while it was collapsed. Defaults to open on first visit so
 * the assistant stays discoverable (spec.md Assumptions); persisted thereafter so a
 * returning user's choice survives a reload. */
export const useAssistantPanelStore = create<AssistantPanelState>()(
  persist(
    (set) => ({
      isOpen: true,
      hasUnreadWhileCollapsed: false,
      open: () => set({ isOpen: true, hasUnreadWhileCollapsed: false }),
      close: () => set({ isOpen: false }),
      toggle: () =>
        set((s) =>
          s.isOpen ? { isOpen: false } : { isOpen: true, hasUnreadWhileCollapsed: false },
        ),
      markUnread: () => set({ hasUnreadWhileCollapsed: true }),
    }),
    { name: 'ask-lucy-assistant-panel' },
  ),
)
