import { apiFetch } from '../../../api/httpClient'

export type AgentType = 'Conversational' | 'Research' | 'Document' | 'Knowledge' | 'Task'
export type AgentStatus = 'Draft' | 'Published' | 'Archived'
export type AgentOutputFormat = 'PlainText' | 'Markdown' | 'Json' | 'StructuredOutput' | 'Files'

export interface AgentInstructions {
  systemInstructions: string | null
  objectives: string | null
  constraints: string | null
  behavioralRules: string | null
  outputRequirements: string | null
  toolUsageRules: string | null
  safetyRules: string | null
}

export const EMPTY_INSTRUCTIONS: AgentInstructions = {
  systemInstructions: null,
  objectives: null,
  constraints: null,
  behavioralRules: null,
  outputRequirements: null,
  toolUsageRules: null,
  safetyRules: null,
}

export interface AgentExecutionPolicy {
  maxSteps: number | null
  maxExecutionDurationSeconds: number | null
  maxTokens: number | null
  maxCost: number | null
  maxToolCalls: number | null
  maxRetries: number | null
}

export const EMPTY_EXECUTION_POLICY: AgentExecutionPolicy = {
  maxSteps: null,
  maxExecutionDurationSeconds: null,
  maxTokens: null,
  maxCost: null,
  maxToolCalls: null,
  maxRetries: null,
}

export interface AgentDetail {
  id: string
  name: string
  description: string | null
  agentType: AgentType
  status: AgentStatus
  instructions: AgentInstructions
  modelProviderId: string | null
  modelId: string | null
  outputFormat: AgentOutputFormat
  executionPolicy: AgentExecutionPolicy
  publishedVersionNumber: number | null
  toolNames: string[]
  knowledgeBaseIds: string[]
  createdAtUtc: string
  modifiedAtUtc: string | null
}

export interface AgentListItem {
  id: string
  name: string
  description: string | null
  agentType: AgentType
  status: AgentStatus
  publishedVersionNumber: number | null
  createdAtUtc: string
  modifiedAtUtc: string | null
}

export interface PagedResult<T> {
  items: T[]
  nextCursor: string | null
}

export interface AgentVersion {
  id: string
  agentId: string
  versionNumber: number
  instructions: AgentInstructions
  modelProviderId: string
  modelId: string
  executionPolicy: AgentExecutionPolicy
  outputFormat: AgentOutputFormat
  changeDescription: string | null
  createdBy: string
  createdAtUtc: string
}

export interface SaveAgentInput {
  name: string
  description: string | null
  agentType: AgentType
  instructions: AgentInstructions
  modelProviderId: string | null
  modelId: string | null
  outputFormat: AgentOutputFormat
  executionPolicy: AgentExecutionPolicy
}

export const getAgent = (id: string) => apiFetch<AgentDetail>(`/agents/${id}`)

export interface ListAgentsParams {
  status?: AgentStatus | null
  agentType?: AgentType | null
  cursor?: string | null
  pageSize?: number
}

export const listAgents = (params: ListAgentsParams = {}) => {
  const query = new URLSearchParams()
  if (params.status) query.set('status', params.status)
  if (params.agentType) query.set('agentType', params.agentType)
  if (params.cursor) query.set('cursor', params.cursor)
  if (params.pageSize) query.set('pageSize', String(params.pageSize))

  return apiFetch<PagedResult<AgentListItem>>(`/agents?${query.toString()}`)
}

export const createAgent = (input: SaveAgentInput) =>
  apiFetch<AgentDetail>('/agents', { method: 'POST', body: JSON.stringify(input) })

export const updateAgent = (id: string, input: SaveAgentInput) =>
  apiFetch<AgentDetail>(`/agents/${id}`, { method: 'PUT', body: JSON.stringify(input) })

export const publishAgentVersion = (id: string, changeDescription: string | null) =>
  apiFetch<AgentVersion>(`/agents/${id}/versions`, { method: 'POST', body: JSON.stringify({ changeDescription }) })

export const listAgentVersions = (id: string) => apiFetch<AgentVersion[]>(`/agents/${id}/versions`)

export const getAgentVersion = (id: string, versionNumber: number) => apiFetch<AgentVersion>(`/agents/${id}/versions/${versionNumber}`)

export const duplicateAgent = (id: string) => apiFetch<AgentDetail>(`/agents/${id}/actions/duplicate`, { method: 'POST' })

export const archiveAgent = (id: string) => apiFetch<AgentDetail>(`/agents/${id}/actions/archive`, { method: 'POST' })

export const restoreAgent = (id: string) => apiFetch<AgentDetail>(`/agents/${id}/actions/restore`, { method: 'POST' })

export const deleteAgent = (id: string) => apiFetch<void>(`/agents/${id}`, { method: 'DELETE' })
