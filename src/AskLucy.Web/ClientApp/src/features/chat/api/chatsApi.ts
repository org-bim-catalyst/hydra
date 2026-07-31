import { apiFetch } from '../../../api/httpClient'
import type { GenerationParameters } from './aiApi'

export interface UserChat {
  id: string
  title: string
  createdAtUtc: string
  modifiedAtUtc: string | null
}

export type ConversationView = 'Active' | 'Archived' | 'Deleted' | 'All'
export type ConversationSort = 'Newest' | 'Oldest' | 'RecentlyUpdated' | 'Alphabetical'

export interface ConversationSummary {
  id: string
  title: string
  createdAtUtc: string
  modifiedAtUtc: string | null
  isArchived: boolean
  isPinned: boolean
  isFavorite: boolean
  isDeleted: boolean
}

export interface PagedResult<T> {
  items: T[]
  nextCursor: string | null
}

export type MessageRole = 'User' | 'Assistant'
export type MessageKind = 'Text' | 'Image' | 'Translation'

export interface PersistedAttachment {
  id: string
  fileName: string
  contentType: string
  accessLocation: string
}

export interface PersistedCitation {
  id: string
  sourceLabel: string
  sourceReference: string | null
}

export interface PersistedMessage {
  id: string
  role: MessageRole
  kind: MessageKind
  content: string
  sourceText: string | null
  createdAtUtc: string
  provider: string | null
  model: string | null
  generationParametersJson: string | null
  inputTokenCount: number | null
  outputTokenCount: number | null
  attachments: PersistedAttachment[]
  citations: PersistedCitation[]
}

export interface SearchChatsParams {
  view?: ConversationView
  pinned?: boolean
  favorite?: boolean
  q?: string
  sort?: ConversationSort
  cursor?: string
  pageSize?: number
}

function toQueryString(params: Record<string, unknown>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined) search.set(key, String(value))
  }
  const query = search.toString()
  return query ? `?${query}` : ''
}

export const searchChats = (params: SearchChatsParams = {}) =>
  apiFetch<PagedResult<ConversationSummary>>(`/chats${toQueryString(params as Record<string, unknown>)}`)

export const createChat = (title: string, sessionId?: string) =>
  apiFetch<UserChat>('/chats', { method: 'POST', body: JSON.stringify({ title, sessionId }) })

export const renameChat = (id: string, title: string) =>
  apiFetch<UserChat>(`/chats/${id}`, { method: 'PATCH', body: JSON.stringify({ title }) })

/** Regular (soft) delete — moves the conversation to Recently Deleted (FR-003). */
export const deleteChat = (id: string) => apiFetch<void>(`/chats/${id}`, { method: 'DELETE' })

export const getChatMessages = (id: string, cursor?: string, pageSize = 50) =>
  apiFetch<PagedResult<PersistedMessage>>(`/chats/${id}/messages${toQueryString({ cursor, pageSize })}`)

/** Restores from Archived or Recently Deleted back to the default view (FR-005a/FR-007). */
export const restoreChat = (id: string) =>
  apiFetch<ConversationSummary>(`/chats/${id}/actions/restore`, { method: 'POST' })

export const archiveChat = (id: string) =>
  apiFetch<ConversationSummary>(`/chats/${id}/actions/archive`, { method: 'POST' })

export const pinChat = (id: string) => apiFetch<ConversationSummary>(`/chats/${id}/actions/pin`, { method: 'POST' })

export const unpinChat = (id: string) => apiFetch<ConversationSummary>(`/chats/${id}/actions/unpin`, { method: 'POST' })

export const favoriteChat = (id: string) =>
  apiFetch<ConversationSummary>(`/chats/${id}/actions/favorite`, { method: 'POST' })

export const unfavoriteChat = (id: string) =>
  apiFetch<ConversationSummary>(`/chats/${id}/actions/unfavorite`, { method: 'POST' })

export const duplicateChat = (id: string) =>
  apiFetch<ConversationSummary>(`/chats/${id}/actions/duplicate`, { method: 'POST' })

/** Clears all messages from a conversation, keeping the conversation and its title (FR-011). Requires confirmation. */
export const clearChatMessages = (id: string) =>
  apiFetch<void>(`/chats/${id}/actions/clear`, { method: 'POST', body: JSON.stringify({ confirm: true }) })

/** Permanent delete (FR-004) — irreversible. Requires explicit confirmation. */
export const purgeChat = (id: string) =>
  apiFetch<void>(`/chats/${id}/actions/purge`, { method: 'DELETE', body: JSON.stringify({ confirm: true }) })

/** specs/005-multi-provider-ai-engine FR-009 — applies to messages sent after this call only; prior messages keep their original attribution (FR-011). */
export const updateChatModelSelection = (
  id: string,
  providerId: string,
  modelId: string,
  generationParameters?: GenerationParameters,
) =>
  apiFetch<void>(`/chats/${id}/model-selection`, {
    method: 'PATCH',
    body: JSON.stringify({ providerId, modelId, generationParameters }),
  })

/** Downloads a structured export of the conversation (FR-025). */
export async function exportChat(id: string): Promise<Blob> {
  const { API_BASE_URL } = await import('../../../api/httpClient')
  const { useAuthStore } = await import('../../../store/authStore')
  const accessToken = useAuthStore.getState().accessToken

  const response = await fetch(`${API_BASE_URL}/chats/${id}/export`, {
    headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
  })

  if (!response.ok) {
    throw new Error(`Export failed with ${response.status}`)
  }

  return response.blob()
}
