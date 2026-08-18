import { apiFetch } from '../../../api/httpClient'

/** contracts/projects-api.md (spec.md FR-002a, User Story 5). */
export interface Project {
  id: string
  name: string
  createdAtUtc: string
}

export interface ProjectsResult {
  items: Project[]
  nextCursor: string | null
}

function toQueryString(params: Record<string, unknown>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  }
  const q = search.toString()
  return q ? `?${q}` : ''
}

export const listProjects = (cursor?: string, pageSize = 50) =>
  apiFetch<ProjectsResult>(`/projects${toQueryString({ cursor, pageSize })}`)

export const createProject = (name: string) => apiFetch<Project>('/projects', { method: 'POST', body: JSON.stringify({ name }) })

export const renameProject = (id: string, name: string) =>
  apiFetch<void>(`/projects/${id}`, { method: 'PUT', body: JSON.stringify({ name }) })

export const deleteProject = (id: string) => apiFetch<void>(`/projects/${id}`, { method: 'DELETE' })

/** contracts/projects-api.md — `PUT /api/v1/chats/{chatId}/project`. Pass `null` to remove the conversation from its Project. */
export const assignChatToProject = (chatId: string, projectId: string | null) =>
  apiFetch<void>(`/chats/${chatId}/project`, { method: 'PUT', body: JSON.stringify({ projectId }) })
