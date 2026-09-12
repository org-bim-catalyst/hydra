import { create } from 'zustand'
import type { ViewerContent } from './ViewerContent'

interface ContentState {
  content: ViewerContent[]
  upsert: (item: ViewerContent) => void
  remove: (id: string) => void
}

/** data-model.md "Viewer Content" — session-scoped only, no `persist` middleware, mirroring every
 * other viewer store's convention (`viewerEngineStore`, `floatingPanelStore`). */
export const useContentStore = create<ContentState>()((set) => ({
  content: [],

  upsert: (item) =>
    set((s) => ({
      content: [...s.content.filter((existing) => existing.id !== item.id), item],
    })),

  remove: (id) => set((s) => ({ content: s.content.filter((existing) => existing.id !== id) })),
}))
