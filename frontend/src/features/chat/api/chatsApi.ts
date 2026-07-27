import { apiFetch } from '../../../api/httpClient'

export interface UserChat {
  id: string
  title: string
  createdAtUtc: string
  modifiedAtUtc: string | null
}

export const listChats = () => apiFetch<UserChat[]>('/chats')

export const createChat = (title: string, sessionId?: string) =>
  apiFetch<UserChat>('/chats', { method: 'POST', body: JSON.stringify({ title, sessionId }) })

export const renameChat = (id: string, title: string) =>
  apiFetch<UserChat>(`/chats/${id}`, { method: 'PATCH', body: JSON.stringify({ title }) })

export const deleteChat = (id: string) => apiFetch<void>(`/chats/${id}`, { method: 'DELETE' })
