import { create } from 'zustand'

/** specs/027-immersive-viewer-platform research.md Decision 4: renamed from the original
 * '2D'|'3D' placeholder values — this control now drives the real viewer's isometric/plan
 * camera toggle (FR-013) instead of a cosmetic gradient-angle change. */
export type ViewMode = 'isometric' | 'plan'

interface WorkspaceOverlayState {
  expandedControlId: string | null
  /** Controls whose ribbon the user pinned open. A pinned ribbon is expanded independently of
   * `expandedControlId`, so outside clicks and expanding another control never collapse it —
   * only unpinning it, or its own trigger, does. */
  pinnedControlIds: Set<string>
  viewMode: ViewMode
  unreadControlIds: Set<string>
  expand: (id: string) => void
  collapse: () => void
  toggle: (id: string) => void
  togglePin: (id: string) => void
  setViewMode: (mode: ViewMode) => void
  markUnread: (id: string) => void
}

/** Whether `id`'s ribbon is open — the one transient control, or any pinned one. */
export const selectIsControlExpanded = (id: string) => (s: WorkspaceOverlayState) =>
  s.expandedControlId === id || s.pinnedControlIds.has(id)

/** FR-015: single source of truth for which one workspace control is expanded, so no two
 * of the six reusable controls (data-model.md) can ever disagree about what's open.
 * Session-scoped only — no `persist` middleware (research.md #4) — so every visit to
 * `/studio` starts fully collapsed. Supersedes `assistantPanelStore`; the chat control
 * uses `controlId: 'chat'` here instead of its own dedicated store.
 *
 * Pinning is the one exception to "one open at a time": a pinned ribbon stays open while
 * another control is expanded, and `collapse()` (outside click, account menu) leaves it alone. */
export const useWorkspaceOverlayStore = create<WorkspaceOverlayState>()((set, get) => ({
  expandedControlId: null,
  pinnedControlIds: new Set(),
  viewMode: 'isometric',
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
    const { pinnedControlIds, expandedControlId } = get()
    if (pinnedControlIds.has(id)) {
      // The trigger is an explicit close, so it overrides the pin.
      const next = new Set(pinnedControlIds)
      next.delete(id)
      set({ pinnedControlIds: next })
    } else if (expandedControlId === id) {
      get().collapse()
    } else {
      get().expand(id)
    }
  },
  togglePin: (id) =>
    set((s) => {
      const next = new Set(s.pinnedControlIds)
      if (next.has(id)) {
        // Unpinning leaves the ribbon open as the transient control, like a fresh expand.
        next.delete(id)
        return { pinnedControlIds: next, expandedControlId: id }
      }
      next.add(id)
      return {
        pinnedControlIds: next,
        expandedControlId: s.expandedControlId === id ? null : s.expandedControlId,
      }
    }),
  setViewMode: (mode) => set({ viewMode: mode }),
  markUnread: (id) =>
    set((s) => {
      const next = new Set(s.unreadControlIds)
      next.add(id)
      return { unreadControlIds: next }
    }),
}))
