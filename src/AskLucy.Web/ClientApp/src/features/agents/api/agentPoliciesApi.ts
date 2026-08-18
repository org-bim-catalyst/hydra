import { apiFetch } from '../../../api/httpClient'

/** Administrator-managed auto-approval rule (spec.md FR-025/FR-026) — mirrors `AgentPolicyDto`. */
export interface AgentPolicy {
  id: string
  name: string
  description: string | null
  toolName: string
  conditionsJson: string | null
  createdByUserId: string
  isEnabled: boolean
  createdAtUtc: string
  modifiedAtUtc: string | null
}

export interface SaveAgentPolicyInput {
  name: string
  description: string | null
  toolName: string
  conditionsJson: string | null
}

export interface UpdateAgentPolicyInput {
  name: string
  description: string | null
  conditionsJson: string | null
  isEnabled: boolean
}

export const listAgentPolicies = () => apiFetch<AgentPolicy[]>('/admin/agent-policies')

export const createAgentPolicy = (input: SaveAgentPolicyInput) =>
  apiFetch<AgentPolicy>('/admin/agent-policies', { method: 'POST', body: JSON.stringify(input) })

export const updateAgentPolicy = (id: string, input: UpdateAgentPolicyInput) =>
  apiFetch<AgentPolicy>(`/admin/agent-policies/${id}`, { method: 'PUT', body: JSON.stringify(input) })

export const deleteAgentPolicy = (id: string) => apiFetch<void>(`/admin/agent-policies/${id}`, { method: 'DELETE' })
