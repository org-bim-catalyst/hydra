import { apiFetch } from '../../../api/httpClient'

/** specs/047-admin-system-agents — mirrors `AdminSystemAgentDto`. Read-only. */
export interface AdminSystemAgent {
  id: string
  name: string
  systemKey: string | null
  status: 'Draft' | 'Published' | 'Archived'
  publishedVersionNumber: number | null
  lastUpdatedAtUtc: string
}

export const getSystemAgents = () => apiFetch<AdminSystemAgent[]>('/admin/agents/system')
