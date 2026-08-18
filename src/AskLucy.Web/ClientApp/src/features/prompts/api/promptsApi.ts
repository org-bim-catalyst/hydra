import { apiFetch } from '../../../api/httpClient'

export type PromptType =
  | 'Chat'
  | 'System'
  | 'Instruction'
  | 'Summarization'
  | 'Translation'
  | 'Extraction'
  | 'Classification'
  | 'Rag'
  | 'StructuredOutput'

export type PromptStatus = 'Draft' | 'Active' | 'Archived'

export type PromptVariableType =
  | 'String'
  | 'Number'
  | 'Boolean'
  | 'Date'
  | 'Json'
  | 'Text'
  | 'File'
  | 'Conversation'
  | 'KnowledgeBase'

export interface PromptVariable {
  name: string
  description: string | null
  type: PromptVariableType
  isRequired: boolean
  defaultValue: string | null
  exampleValue: string | null
  validationRulesJson: string | null
  orderIndex: number
}

export interface PromptCapabilityRequirements {
  requiresStreaming: boolean
  requiresVision: boolean
  requiresFunctionCalling: boolean
  requiresJsonMode: boolean
  requiresReasoning: boolean
  requiresEmbeddings: boolean
  requiresImageInput: boolean
  requiresImageOutput: boolean
  requiresAudio: boolean
}

export const NO_REQUIRED_CAPABILITIES: PromptCapabilityRequirements = {
  requiresStreaming: false,
  requiresVision: false,
  requiresFunctionCalling: false,
  requiresJsonMode: false,
  requiresReasoning: false,
  requiresEmbeddings: false,
  requiresImageInput: false,
  requiresImageOutput: false,
  requiresAudio: false,
}

export interface PromptVersionRef {
  id: string
  versionNumber: number
}

export interface PromptDetail {
  id: string
  name: string
  description: string | null
  promptType: PromptType
  status: PromptStatus
  systemInstructions: string | null
  developerInstructions: string | null
  userInstructions: string
  contextText: string | null
  examplesText: string | null
  outputInstructions: string | null
  constraints: string | null
  categoryId: string | null
  folderId: string | null
  isFavorite: boolean
  isPinned: boolean
  requiredCapabilities: PromptCapabilityRequirements
  preferredModelKey: string | null
  currentVersion: PromptVersionRef
  variables: PromptVariable[]
  tags: string[]
  usageCount: number
  lastSuccessfulUseAtUtc: string | null
  createdAtUtc: string
  modifiedAtUtc: string | null
}

export interface PromptListItem {
  id: string
  name: string
  description: string | null
  promptType: PromptType
  status: PromptStatus
  categoryId: string | null
  tags: string[]
  isFavorite: boolean
  isPinned: boolean
  usageCount: number
  lastSuccessfulUseAtUtc: string | null
  modifiedAtUtc: string | null
}

export interface PagedResult<T> {
  items: T[]
  nextCursor: string | null
}

export type PromptListView = 'All' | 'Favorites' | 'Pinned' | 'RecentlyUsed' | 'RecentlyModified' | 'Archived'

export interface ListPromptsParams {
  view?: PromptListView
  q?: string
  categoryId?: string | null
  tag?: string | null
  folderId?: string | null
  status?: PromptStatus | null
  cursor?: string | null
  pageSize?: number
}

/** Search/filter/sort/paginate the caller's own prompts (FR-050–FR-053). */
export const listPrompts = (params: ListPromptsParams = {}) => {
  const query = new URLSearchParams()
  query.set('view', params.view ?? 'All')
  if (params.q) query.set('q', params.q)
  if (params.categoryId) query.set('categoryId', params.categoryId)
  if (params.tag) query.set('tag', params.tag)
  if (params.folderId) query.set('folderId', params.folderId)
  if (params.status) query.set('status', params.status)
  if (params.cursor) query.set('cursor', params.cursor)
  if (params.pageSize) query.set('pageSize', String(params.pageSize))

  return apiFetch<PagedResult<PromptListItem>>(`/prompts?${query.toString()}`)
}

export const setFavorite = (id: string, isFavorite: boolean) =>
  apiFetch<void>(`/prompts/${id}/favorite`, { method: 'PUT', body: JSON.stringify({ isFavorite }) })

export const setPinned = (id: string, isPinned: boolean) =>
  apiFetch<void>(`/prompts/${id}/pinned`, { method: 'PUT', body: JSON.stringify({ isPinned }) })

export interface PromptCategory {
  id: string
  name: string
  isPredefined: boolean
}

export const listCategories = () => apiFetch<PromptCategory[]>('/prompts/categories')

export const createCategory = (name: string) =>
  apiFetch<PromptCategory>('/prompts/categories', { method: 'POST', body: JSON.stringify({ name }) })

export const listTags = () => apiFetch<string[]>('/prompts/tags')

export const addTag = (id: string, value: string) =>
  apiFetch<{ id: string; value: string }>(`/prompts/${id}/tags`, { method: 'POST', body: JSON.stringify({ value }) })

export const removeTag = (id: string, tagId: string) =>
  apiFetch<void>(`/prompts/${id}/tags/${tagId}`, { method: 'DELETE' })

export interface PromptPreview {
  systemInstructions: string | null
  developerInstructions: string | null
  userInstructions: string
  contextText: string | null
  outputInstructions: string | null
  constraints: string | null
}

export interface SavePromptInput {
  name: string
  description?: string | null
  promptType: PromptType
  systemInstructions?: string | null
  developerInstructions?: string | null
  userInstructions: string
  contextText?: string | null
  examplesText?: string | null
  outputInstructions?: string | null
  constraints?: string | null
  categoryId?: string | null
  folderId?: string | null
  requiredCapabilities?: PromptCapabilityRequirements
  preferredModelKey?: string | null
  variables?: PromptVariable[]
}

export interface UpdatePromptInput extends SavePromptInput {
  changeDescription?: string | null
}

export const createPrompt = (input: SavePromptInput) =>
  apiFetch<PromptDetail>('/prompts', { method: 'POST', body: JSON.stringify(input) })

export const getPrompt = (id: string) => apiFetch<PromptDetail>(`/prompts/${id}`)

export const updatePrompt = (id: string, input: UpdatePromptInput) =>
  apiFetch<PromptDetail>(`/prompts/${id}`, { method: 'PUT', body: JSON.stringify(input) })

export const deletePrompt = (id: string) => apiFetch<void>(`/prompts/${id}`, { method: 'DELETE' })

export const archivePrompt = (id: string) => apiFetch<void>(`/prompts/${id}/actions/archive`, { method: 'POST' })

export const restorePrompt = (id: string) => apiFetch<void>(`/prompts/${id}/actions/restore`, { method: 'POST' })

export const duplicatePrompt = (id: string) => apiFetch<PromptDetail>(`/prompts/${id}/actions/duplicate`, { method: 'POST' })

export const previewPrompt = (id: string, variableValues: Record<string, string | null>) =>
  apiFetch<PromptPreview>(`/prompts/${id}/preview`, {
    method: 'POST',
    body: JSON.stringify({ variableValues }),
  })

// --- Export / Import (spec.md FR-070–FR-072, contracts/prompts-api.md) ---

export interface PromptExportEntry {
  name: string
  description: string | null
  promptType: PromptType
  systemInstructions: string | null
  developerInstructions: string | null
  userInstructions: string
  contextText: string | null
  examplesText: string | null
  outputInstructions: string | null
  constraints: string | null
  requiredCapabilities: PromptCapabilityRequirements
  preferredModelKey: string | null
  variables: PromptVariable[]
  tags: string[]
}

export interface PromptExportFile {
  schemaVersion: number
  prompts: PromptExportEntry[]
}

/** Downloads a portable export file for one or more of the caller's own prompts (FR-070). */
export async function exportPrompts(promptIds: string[]): Promise<Blob> {
  const { API_BASE_URL } = await import('../../../api/httpClient')
  const { useAuthStore } = await import('../../../store/authStore')
  const accessToken = useAuthStore.getState().accessToken

  const response = await fetch(`${API_BASE_URL}/prompts/export`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
    },
    body: JSON.stringify({ promptIds }),
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => undefined)
    throw new Error(problem?.detail ?? problem?.title ?? `Export failed with ${response.status}`)
  }

  return response.blob()
}

/** Imports a previously-exported file — atomic, all-or-nothing validation (FR-071/FR-072). */
export const importPrompts = (file: PromptExportFile) =>
  apiFetch<PromptListItem[]>('/prompts/import', { method: 'POST', body: JSON.stringify(file) })
