import { create } from 'zustand'

export type ViewMode = '2D' | '3D'

interface WorkspaceOverlayState {
  expandedControlId: string | null
  viewMode: ViewMode
  unreadControlIds: Set<string>
  expand: (id: string) => void
  collapse: () => void
  toggle: (id: string) => void
  setViewMode: (mode: ViewMode) => void
  markUnread: (id: string) => void
}

/** FR-015: single source of truth for which one workspace control is expanded, so no two
 * of the six reusable controls (data-model.md) can ever disagree about what's open.
 * Session-scoped only — no `persist` middleware (research.md #4) — so every visit to
 * `/studio` starts fully collapsed. Supersedes `assistantPanelStore`; the chat control
 * uses `controlId: 'chat'` here instead of its own dedicated store. */
export const useWorkspaceOverlayStore = create<WorkspaceOverlayState>()((set, get) => ({
  expandedControlId: null,
  viewMode: '3D',
  unreadControlIds: new Set(),
  expand: (id) =>
    set((s) => {
      if (!s.unreadControlIds.has(id)) {
        return { expandedControlId: id }
      }
      const next = new Set(s.unreadControlIds)
      next.delete(id)
      return { expandedControlId: id, unreadControlIds: next }
    }),
  collapse: () => set({ expandedControlId: null }),
  toggle: (id) => {
    if (get().expandedControlId === id) {
      get().collapse()
    } else {
      get().expand(id)
    }
  },
  setViewMode: (mode) => set({ viewMode: mode }),
  markUnread: (id) =>
    set((s) => {
      const next = new Set(s.unreadControlIds)
      next.add(id)
      return { unreadControlIds: next }
    }),
}))
