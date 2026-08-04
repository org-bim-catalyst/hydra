import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { KnowledgeBaseSort } from '../api/knowledgeBasesApi'

export type KnowledgeBaseLayout = 'grid' | 'list'

interface KnowledgeBaseDashboardState {
  query: string
  categoryId: string | null
  tag: string | null
  sort: KnowledgeBaseSort
  sortDescending: boolean
  layout: KnowledgeBaseLayout
  setQuery: (query: string) => void
  setCategoryId: (categoryId: string | null) => void
  setTag: (tag: string | null) => void
  setSort: (sort: KnowledgeBaseSort) => void
  setSortDescending: (sortDescending: boolean) => void
  setLayout: (layout: KnowledgeBaseLayout) => void
  clearFilters: () => void
}

/** UI-only dashboard state (US4) — persisted so a user's chosen layout/sort survives navigation and reloads (FR-026); search/category/tag filters are session-scoped, not persisted, since a stale filter silently hiding results on next visit would be surprising. */
export const useKnowledgeBaseDashboardStore = create<KnowledgeBaseDashboardState>()(
  persist(
    (set) => ({
      query: '',
      categoryId: null,
      tag: null,
      sort: 'RecentlyUpdated',
      sortDescending: true,
      layout: 'grid',
      setQuery: (query) => set({ query }),
      setCategoryId: (categoryId) => set({ categoryId }),
      setTag: (tag) => set({ tag }),
      setSort: (sort) => set({ sort }),
      setSortDescending: (sortDescending) => set({ sortDescending }),
      setLayout: (layout) => set({ layout }),
      clearFilters: () => set({ query: '', categoryId: null, tag: null }),
    }),
    {
      name: 'ask-lucy-knowledge-base-dashboard',
      partialize: (state) => ({ sort: state.sort, sortDescending: state.sortDescending, layout: state.layout }),
    },
  ),
)
