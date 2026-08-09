import { create } from 'zustand'
import { persist } from 'zustand/middleware'

export type MemoryLayout = 'grid' | 'list'

interface MemoryCenterState {
  query: string
  category: string | null
  state: string | null
  projectId: string | null
  layout: MemoryLayout
  setQuery: (query: string) => void
  setCategory: (category: string | null) => void
  setState: (state: string | null) => void
  setProjectId: (projectId: string | null) => void
  setLayout: (layout: MemoryLayout) => void
  clearFilters: () => void
}

/** UI-only Memory Center state (tasks.md T056), mirroring `knowledgeBaseDashboardStore.ts` — layout is persisted across reloads; search/category/state/project filters are session-scoped, not persisted, so a stale filter never silently hides memories on next visit. */
export const useMemoryCenterStore = create<MemoryCenterState>()(
  persist(
    (set) => ({
      query: '',
      category: null,
      state: null,
      projectId: null,
      layout: 'list',
      setQuery: (query) => set({ query }),
      setCategory: (category) => set({ category }),
      setState: (state) => set({ state }),
      setProjectId: (projectId) => set({ projectId }),
      setLayout: (layout) => set({ layout }),
      clearFilters: () => set({ query: '', category: null, state: null, projectId: null }),
    }),
    {
      name: 'ask-lucy-memory-center',
      partialize: (state) => ({ layout: state.layout }),
    },
  ),
)
