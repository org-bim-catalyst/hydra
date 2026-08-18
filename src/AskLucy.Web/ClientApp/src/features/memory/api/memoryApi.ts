import { apiFetch } from '../../../api/httpClient'

/** contracts/memories-api.md — the "why does Lucy know this" trace (spec.md FR-014, User Story 1). `content` is a snapshot taken when the memory was used, so it stays meaningful even if the source memory is later edited/archived/deleted. */
export interface MemoryReference {
  memoryId: string
  content: string
  relevanceScore: number
}

export const getMemoryReferences = (chatId: string, messageId: string) =>
  apiFetch<MemoryReference[]>(`/chats/${chatId}/messages/${messageId}/memory-references`)

export type MemoryCategory = 'UserPreference' | 'PersonalFact' | 'ProjectContext' | 'ConversationDerived'
export type MemoryLifecycleState = 'PendingApproval' | 'Active' | 'Archived'
export type MemorySourceType = 'ExplicitUserStatement' | 'PassiveConversationAnalysis' | 'ProjectConfiguration' | 'Integration'

/** contracts/memories-api.md — the Memory Center list item (spec.md FR-017, User Story 2 AC1). */
export interface MemoryListItem {
  id: string
  category: MemoryCategory
  content: string
  state: MemoryLifecycleState
  isSensitive: boolean
  projectId: string | null
  projectName: string | null
  sourceType: MemorySourceType
  sourceConversationId: string | null
  importance: number
  confidence: number
  lastReinforcedAtUtc: string
  createdAtUtc: string
}

export interface MemoryListResult {
  results: MemoryListItem[]
  nextCursor: string | null
  totalCount: number
}

export interface MemoryVersion {
  previousContent: string
  changeReason: 'UserEdit' | 'ConflictResolutionSupersede' | 'SystemReinforcement'
  changedAtUtc: string
  changedByActor: string
}

/** spec.md FR-016, User Story 6 — present only while an ambiguous conflict awaits the user's confirmation. */
export interface OpenMemoryConflict {
  id: string
  conflictType: 'DirectContradiction' | 'AmbiguousSupersedeOrSupplement'
  existingMemoryId: string
  newMemoryId: string | null
  detectedAtUtc: string
}

export interface MemoryDetail {
  id: string
  category: MemoryCategory
  content: string
  state: MemoryLifecycleState
  isSensitive: boolean
  projectId: string | null
  importance: number
  confidence: number
  history: MemoryVersion[]
  openConflict: OpenMemoryConflict | null
}

export interface ListMemoriesParams {
  category?: MemoryCategory | null
  state?: MemoryLifecycleState | null
  /** A real project id, the literal `'general'` (memories with no project), or omitted for every scope. */
  projectId?: string | null
  query?: string
  cursor?: string
  pageSize?: number
}

function toQueryString(params: Record<string, unknown>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  }
  const q = search.toString()
  return q ? `?${q}` : ''
}

export const listMemories = (params: ListMemoriesParams = {}) =>
  apiFetch<MemoryListResult>(`/memories${toQueryString(params as Record<string, unknown>)}`)

export const getMemory = (id: string) => apiFetch<MemoryDetail>(`/memories/${id}`)

export const editMemory = (id: string, content: string) =>
  apiFetch<void>(`/memories/${id}`, { method: 'PUT', body: JSON.stringify({ content }) })

export const deleteMemory = (id: string) => apiFetch<void>(`/memories/${id}`, { method: 'DELETE' })

export const approveMemory = (id: string) => apiFetch<void>(`/memories/${id}/actions/approve`, { method: 'POST' })

export const rejectMemory = (id: string) => apiFetch<void>(`/memories/${id}/actions/reject`, { method: 'POST' })

/** contracts/memories-api.md — `POST /api/v1/memories/{id}/actions/resolve-conflict` (spec.md FR-016, User Story 6 AC2/AC3). */
export type MemoryConflictResolution = 'KeepExisting' | 'KeepNew' | 'KeepBoth'

export const resolveMemoryConflict = (id: string, resolution: MemoryConflictResolution) =>
  apiFetch<void>(`/memories/${id}/actions/resolve-conflict`, { method: 'POST', body: JSON.stringify({ resolution }) })

export type MemoryApprovalMode = 'Automatic' | 'Manual' | 'Disabled'

/** contracts/memory-privacy-api.md — `GET/PUT /api/v1/memories/preferences` (spec.md FR-007, FR-022, FR-025). */
export interface MemoryCategoryPreference {
  category: MemoryCategory
  approvalMode: MemoryApprovalMode
  isEnabled: boolean
}

export interface MemoryPreferences {
  memoryEnabled: boolean
  categories: MemoryCategoryPreference[]
}

export interface UpdateMemoryPreferencesInput {
  memoryEnabled?: boolean
  categories?: { category: MemoryCategory; approvalMode?: MemoryApprovalMode; isEnabled?: boolean }[]
}

export const getMemoryPreferences = () => apiFetch<MemoryPreferences>('/memories/preferences')

export const updateMemoryPreferences = (input: UpdateMemoryPreferencesInput) =>
  apiFetch<void>('/memories/preferences', { method: 'PUT', body: JSON.stringify(input) })

/** contracts/memory-privacy-api.md — `GET /api/v1/memories/notifications` (FR-006a). */
export interface MemoryNotification {
  id: string
  memoryId: string | null
  eventType: 'AutoCreated' | 'AutoApproved' | 'ConflictNeedsConfirmation'
  message: string
  createdAtUtc: string
  readAtUtc: string | null
}

export interface MemoryNotificationsResult {
  items: MemoryNotification[]
  nextCursor: string | null
}

export const listMemoryNotifications = (cursor?: string, pageSize = 20) =>
  apiFetch<MemoryNotificationsResult>(`/memories/notifications${toQueryString({ cursor, pageSize })}`)

export const markNotificationRead = (id: string) =>
  apiFetch<void>(`/memories/notifications/${id}/actions/mark-read`, { method: 'POST' })

/** contracts/memory-privacy-api.md — `POST /api/v1/memories/actions/clear-all` (FR-023). Irreversible; requires explicit confirmation. */
export const clearAllMemories = () => apiFetch<void>('/memories/actions/clear-all', { method: 'POST', body: JSON.stringify({ confirm: true }) })

/** contracts/memory-privacy-api.md — `POST /api/v1/memories/actions/export` (FR-024). */
export const requestMemoryExport = () =>
  apiFetch<{ exportJobId: string }>('/memories/actions/export', { method: 'POST' })

export type MemoryExportStatus = 'Processing' | 'Ready' | 'Failed'

export interface MemoryExportStatusResult {
  status: MemoryExportStatus
  downloadUrl: string | null
}

export const getMemoryExportStatus = (exportJobId: string) =>
  apiFetch<MemoryExportStatusResult>(`/memories/exports/${exportJobId}`)
