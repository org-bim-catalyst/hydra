import { apiFetch } from '../../../api/httpClient'
import type { PagedResult } from './knowledgeBasesApi'

export interface KnowledgeBaseFolder {
  id: string
  knowledgeBaseId: string
  parentFolderId: string | null
  name: string
  depth: number
}

export interface KnowledgeBaseDocument {
  id: string
  knowledgeBaseId: string
  folderId: string | null
  fileName: string
  contentType: string
  sizeBytes: number
  pageCount: number | null
  processingStatus: 'Uploaded' | 'Ready' | 'Failed'
  uploadedAtUtc: string
}

export interface FolderTree {
  folders: KnowledgeBaseFolder[]
  rootDocuments: KnowledgeBaseDocument[]
}

export const getFolderTree = (knowledgeBaseId: string) =>
  apiFetch<FolderTree>(`/knowledge-bases/${knowledgeBaseId}/folders`)

export const createFolder = (knowledgeBaseId: string, name: string, parentFolderId: string | null) =>
  apiFetch<KnowledgeBaseFolder>(`/knowledge-bases/${knowledgeBaseId}/folders`, {
    method: 'POST',
    body: JSON.stringify({ name, parentFolderId }),
  })

export const renameFolder = (knowledgeBaseId: string, folderId: string, name: string) =>
  apiFetch<KnowledgeBaseFolder>(`/knowledge-bases/${knowledgeBaseId}/folders/${folderId}`, {
    method: 'PATCH',
    body: JSON.stringify({ name }),
  })

export const moveFolder = (knowledgeBaseId: string, folderId: string, newParentFolderId: string | null) =>
  apiFetch<KnowledgeBaseFolder>(`/knowledge-bases/${knowledgeBaseId}/folders/${folderId}/actions/move`, {
    method: 'POST',
    body: JSON.stringify({ newParentFolderId }),
  })

/** Requires `confirm: true` when the folder is non-empty (FR-015) — the caller retries with confirm after the first attempt's 400 explains what the folder contains. */
export const deleteFolder = (knowledgeBaseId: string, folderId: string, confirm = false) =>
  apiFetch<void>(`/knowledge-bases/${knowledgeBaseId}/folders/${folderId}`, {
    method: 'DELETE',
    body: JSON.stringify({ confirm }),
  })

export const listDocuments = (knowledgeBaseId: string, folderId: string | null) =>
  apiFetch<PagedResult<KnowledgeBaseDocument>>(
    `/knowledge-bases/${knowledgeBaseId}/documents${folderId ? `?folderId=${folderId}` : ''}`,
  )

export const moveDocument = (knowledgeBaseId: string, documentId: string, newFolderId: string | null) =>
  apiFetch<KnowledgeBaseDocument>(`/knowledge-bases/${knowledgeBaseId}/documents/${documentId}/actions/move`, {
    method: 'POST',
    body: JSON.stringify({ newFolderId }),
  })

export const deleteDocument = (knowledgeBaseId: string, documentId: string) =>
  apiFetch<void>(`/knowledge-bases/${knowledgeBaseId}/documents/${documentId}`, { method: 'DELETE' })

/** multipart/form-data upload (constitution §8 — content validated server-side by magic-byte signature, not trusted from the client). */
export async function uploadDocument(knowledgeBaseId: string, file: File, folderId: string | null): Promise<KnowledgeBaseDocument> {
  const { API_BASE_URL } = await import('../../../api/httpClient')
  const { useAuthStore } = await import('../../../store/authStore')
  const accessToken = useAuthStore.getState().accessToken

  const formData = new FormData()
  formData.append('file', file)
  if (folderId) formData.append('folderId', folderId)

  const response = await fetch(`${API_BASE_URL}/knowledge-bases/${knowledgeBaseId}/documents`, {
    method: 'POST',
    headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
    body: formData,
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => undefined)
    throw new Error(problem?.detail ?? problem?.title ?? 'Upload failed')
  }

  return response.json()
}
