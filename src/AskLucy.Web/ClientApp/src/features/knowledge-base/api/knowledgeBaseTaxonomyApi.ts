import { apiFetch } from '../../../api/httpClient'

export interface KnowledgeBaseCategory {
  id: string
  name: string
  isPredefined: boolean
}

/** The 8 predefined (shared) categories plus the caller's own private custom ones (FR-017/FR-018/FR-038). */
export const listKnowledgeBaseCategories = () => apiFetch<KnowledgeBaseCategory[]>('/knowledge-bases/categories')

/** Private to the creating user (FR-038). Rejects a duplicate name (case-insensitive) for the same owner with 409. */
export const createKnowledgeBaseCategory = (name: string) =>
  apiFetch<KnowledgeBaseCategory>('/knowledge-bases/categories', { method: 'POST', body: JSON.stringify({ name }) })

/** Every knowledge base referencing this category falls back to Uncategorized (FR-021). Predefined categories can never be deleted (404). */
export const deleteKnowledgeBaseCategory = (id: string) =>
  apiFetch<void>(`/knowledge-bases/categories/${id}`, { method: 'DELETE' })

/** The caller's distinct tag values across all their knowledge bases (FR-020), optionally prefix-filtered. */
export const listKnowledgeBaseTags = (prefix?: string) =>
  apiFetch<string[]>(`/knowledge-bases/tags${prefix ? `?q=${encodeURIComponent(prefix)}` : ''}`)
