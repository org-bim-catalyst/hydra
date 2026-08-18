import { apiFetch } from '../../../api/httpClient'

export interface PromptFolder {
  id: string
  parentFolderId: string | null
  name: string
  depth: number
}

export const getFolderTree = () => apiFetch<PromptFolder[]>('/prompt-folders')

export const createFolder = (name: string, parentFolderId: string | null) =>
  apiFetch<PromptFolder>('/prompt-folders', { method: 'POST', body: JSON.stringify({ name, parentFolderId }) })

export const renameFolder = (id: string, name: string) =>
  apiFetch<PromptFolder>(`/prompt-folders/${id}`, { method: 'PUT', body: JSON.stringify({ name }) })

export const moveFolder = (id: string, newParentFolderId: string | null) =>
  apiFetch<PromptFolder>(`/prompt-folders/${id}/move`, { method: 'PUT', body: JSON.stringify({ newParentFolderId }) })

export const deleteFolder = (id: string) => apiFetch<void>(`/prompt-folders/${id}`, { method: 'DELETE' })
