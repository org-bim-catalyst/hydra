import { apiFetch } from '../../../api/httpClient'

export type KnowledgeBaseStatus = 'Draft' | 'Active' | 'Archived'

export interface KnowledgeBaseSummary {
  id: string
  name: string
  description: string | null
  status: KnowledgeBaseStatus
  color: string | null
  icon: string | null
  categoryId: string | null
  tags: string[]
  isFavorite: boolean
  isPinned: boolean
  documentCount: number
  totalPageCount: number
  storageSizeBytes: number
  createdAtUtc: string
  lastUpdatedAtUtc: string
  isDeleted: boolean
}

export interface KnowledgeBaseDetail extends KnowledgeBaseSummary {
  ownerId: string
  notes: string | null
}

export interface CreateKnowledgeBaseInput {
  name: string
  description?: string | null
  color?: string | null
  icon?: string | null
  categoryId?: string | null
  tags?: string[]
}

export interface UpdateKnowledgeBaseDetailsInput {
  name: string
  description?: string | null
  color?: string | null
  icon?: string | null
  categoryId?: string | null
  tags?: string[]
  notes?: string | null
}

export interface PagedResult<T> {
  items: T[]
  nextCursor: string | null
}

export type KnowledgeBaseListView = 'Active' | 'Archived' | 'Deleted'

export type KnowledgeBaseSort = 'Name' | 'RecentlyUpdated' | 'Created' | 'DocumentCount' | 'StorageSize'

export interface KnowledgeBaseDashboardSummary {
  totalKnowledgeBases: number
  totalDocuments: number
  totalStorageBytes: number
  recentCount: number
  favoritesCount: number
  pinnedCount: number
  archivedCount: number
}

export interface SearchKnowledgeBasesParams {
  view?: KnowledgeBaseListView
  q?: string
  categoryId?: string | null
  tag?: string | null
  favorite?: boolean
  pinned?: boolean
  sort?: KnowledgeBaseSort
  sortDescending?: boolean
  cursor?: string | null
  pageSize?: number
}

/** Full search/filter/sort/cursor-pagination shape (FR-022–FR-024, US4) — `GET /api/v1/knowledge-bases`. */
export const searchKnowledgeBases = (params: SearchKnowledgeBasesParams = {}) => {
  const query = new URLSearchParams()
  query.set('view', params.view ?? 'Active')
  if (params.q) query.set('q', params.q)
  if (params.categoryId) query.set('categoryId', params.categoryId)
  if (params.tag) query.set('tag', params.tag)
  if (params.favorite) query.set('favorite', 'true')
  if (params.pinned) query.set('pinned', 'true')
  if (params.sort) query.set('sort', params.sort)
  if (params.sortDescending !== undefined) query.set('sortDescending', String(params.sortDescending))
  if (params.cursor) query.set('cursor', params.cursor)
  if (params.pageSize) query.set('pageSize', String(params.pageSize))

  return apiFetch<PagedResult<KnowledgeBaseSummary>>(`/knowledge-bases?${query.toString()}`)
}

/** Dashboard summary statistics cards (FR-029) — cached server-side per-user for 60s (research.md Decision 7). */
export const getKnowledgeBaseDashboardSummary = () =>
  apiFetch<KnowledgeBaseDashboardSummary>('/knowledge-bases/dashboard-summary')

export const createKnowledgeBase = (input: CreateKnowledgeBaseInput) =>
  apiFetch<KnowledgeBaseSummary>('/knowledge-bases', { method: 'POST', body: JSON.stringify(input) })

export const getKnowledgeBase = (id: string) => apiFetch<KnowledgeBaseDetail>(`/knowledge-bases/${id}`)

export const updateKnowledgeBaseDetails = (id: string, input: UpdateKnowledgeBaseDetailsInput) =>
  apiFetch<KnowledgeBaseSummary>(`/knowledge-bases/${id}`, { method: 'PATCH', body: JSON.stringify(input) })

/** Regular (soft) delete — moves the knowledge base to the Deleted view (FR-005). No confirmation required; reversible via Restore. */
export const deleteKnowledgeBase = (id: string) => apiFetch<void>(`/knowledge-bases/${id}`, { method: 'DELETE' })

/** Restores from Archived or Deleted back to Active — cancels a pending automatic purge if soft-deleted (FR-036 edge case). */
export const restoreKnowledgeBase = (id: string) =>
  apiFetch<KnowledgeBaseSummary>(`/knowledge-bases/${id}/actions/restore`, { method: 'POST' })

/** Draft -> Active (research.md Decision 1). */
export const activateKnowledgeBase = (id: string) =>
  apiFetch<KnowledgeBaseSummary>(`/knowledge-bases/${id}/actions/activate`, { method: 'POST' })

/** Active -> Archived (FR-004). */
export const archiveKnowledgeBase = (id: string) =>
  apiFetch<KnowledgeBaseSummary>(`/knowledge-bases/${id}/actions/archive`, { method: 'POST' })

/** Permanent delete (FR-036) — irreversible. Requires explicit confirmation. */
export const purgeKnowledgeBase = (id: string) =>
  apiFetch<void>(`/knowledge-bases/${id}/actions/purge`, { method: 'DELETE', body: JSON.stringify({ confirm: true }) })

/** FR-027/FR-028 — surfaces the knowledge base in the dashboard's Favorites section. */
export const favoriteKnowledgeBase = (id: string) =>
  apiFetch<KnowledgeBaseSummary>(`/knowledge-bases/${id}/actions/favorite`, { method: 'POST' })

export const unfavoriteKnowledgeBase = (id: string) =>
  apiFetch<KnowledgeBaseSummary>(`/knowledge-bases/${id}/actions/unfavorite`, { method: 'POST' })

/** FR-027/FR-028 — pinned knowledge bases sort first within every list/search result (see `KnowledgeBaseRepository.SearchAsync`). */
export const pinKnowledgeBase = (id: string) =>
  apiFetch<KnowledgeBaseSummary>(`/knowledge-bases/${id}/actions/pin`, { method: 'POST' })

export const unpinKnowledgeBase = (id: string) =>
  apiFetch<KnowledgeBaseSummary>(`/knowledge-bases/${id}/actions/unpin`, { method: 'POST' })

/** Deep copy — new knowledge base, own id, `status: Draft` (FR-032/FR-037). The source is unchanged. */
export const duplicateKnowledgeBase = (id: string) =>
  apiFetch<KnowledgeBaseSummary>(`/knowledge-bases/${id}/actions/duplicate`, { method: 'POST' })

/** Downloads a structured JSON metadata export (FR-033) — mirrors `chatsApi.exportChat`'s raw-`fetch`-for-a-`Blob` pattern (bypassing `apiFetch`, which always parses JSON). */
export async function exportKnowledgeBase(id: string): Promise<Blob> {
  const { API_BASE_URL } = await import('../../../api/httpClient')
  const { useAuthStore } = await import('../../../store/authStore')
  const accessToken = useAuthStore.getState().accessToken

  const response = await fetch(`${API_BASE_URL}/knowledge-bases/${id}/export`, {
    headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
  })

  if (!response.ok) {
    throw new Error(`Export failed with ${response.status}`)
  }

  return response.blob()
}
