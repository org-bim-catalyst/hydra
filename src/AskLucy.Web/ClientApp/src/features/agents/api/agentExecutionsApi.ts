import { apiFetch } from '../../../api/httpClient'

export type AgentExecutionStatus = 'Queued' | 'Running' | 'Paused' | 'WaitingForApproval' | 'Completed' | 'Failed' | 'Cancelled'
export type AgentConversationIntegrationMode = 'Standalone' | 'NewConversation' | 'ExistingConversation'
export type AgentExecutionStepStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Skipped' | 'Cancelled' | 'WaitingForApproval'
export type AgentExecutionStepType = 'ToolCall' | 'ModelReasoning' | 'Validation'

export interface AgentExecutionStep {
  id: string
  stepIndex: number
  description: string
  stepType: AgentExecutionStepType
  status: AgentExecutionStepStatus
  dependsOnStepId: string | null
  toolName: string | null
  outputJson: string | null
  startedAtUtc: string | null
  completedAtUtc: string | null
}

export interface AgentApproval {
  id: string
  agentToolCallId: string | null
  intendedActionDescription: string
  intendedParametersJson: string
  decision: 'Pending' | 'Approved' | 'Rejected'
  decidedByUserId: string | null
  wasPolicyBased: boolean
  decidedAtUtc: string | null
}

export interface AgentExecutionError {
  id: string
  category: string
  message: string
  retryCount: number
  occurredAtUtc: string
}

export interface AgentExecutionDetail {
  id: string
  agentId: string
  agentVersionId: string
  agentVersionNumber: number
  objective: string
  status: AgentExecutionStatus
  isTestExecution: boolean
  conversationIntegrationMode: AgentConversationIntegrationMode
  userChatId: string | null
  finalOutputText: string | null
  finalOutputJson: string | null
  startedAtUtc: string | null
  completedAtUtc: string | null
  terminationReason: string | null
  steps: AgentExecutionStep[]
  approvals: AgentApproval[]
  errors: AgentExecutionError[]
  inputTokenCount: number | null
  outputTokenCount: number | null
  estimatedCost: number | null
  createdAtUtc: string
}

export interface AgentExecutionSummary {
  id: string
  agentId: string
  status: AgentExecutionStatus
  isTestExecution: boolean
  createdAtUtc: string
}

export interface StartAgentExecutionInput {
  agentId: string
  agentVersionNumber: number | null
  objective: string
  conversationIntegrationMode: AgentConversationIntegrationMode
  userChatId: string | null
  isTestExecution?: boolean
}

export const startAgentExecution = (input: StartAgentExecutionInput) =>
  apiFetch<AgentExecutionSummary>('/agent-executions', { method: 'POST', body: JSON.stringify(input) })

export const getAgentExecution = (id: string) => apiFetch<AgentExecutionDetail>(`/agent-executions/${id}`)

export const approveAgentAction = (executionId: string, approvalId: string) =>
  apiFetch<AgentApproval>(`/agent-executions/${executionId}/approvals/${approvalId}/approve`, { method: 'POST' })

export const rejectAgentAction = (executionId: string, approvalId: string, reason: string | null) =>
  apiFetch<AgentApproval>(`/agent-executions/${executionId}/approvals/${approvalId}/reject`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  })

export const pauseAgentExecution = (executionId: string) =>
  apiFetch<void>(`/agent-executions/${executionId}/pause`, { method: 'POST' })

export const resumeAgentExecution = (executionId: string) =>
  apiFetch<void>(`/agent-executions/${executionId}/resume`, { method: 'POST' })

export const cancelAgentExecution = (executionId: string) =>
  apiFetch<void>(`/agent-executions/${executionId}/cancel`, { method: 'POST' })

export interface PagedResult<T> {
  items: T[]
  nextCursor: string | null
}

export interface ListAgentExecutionsParams {
  agentId?: string | null
  status?: AgentExecutionStatus | null
  isTestExecution?: boolean | null
  cursor?: string | null
  pageSize?: number
}

export const listAgentExecutions = (params: ListAgentExecutionsParams = {}) => {
  const query = new URLSearchParams()
  if (params.agentId) query.set('agentId', params.agentId)
  if (params.status) query.set('status', params.status)
  if (params.isTestExecution !== undefined && params.isTestExecution !== null) query.set('isTestExecution', String(params.isTestExecution))
  if (params.cursor) query.set('cursor', params.cursor)
  if (params.pageSize) query.set('pageSize', String(params.pageSize))

  return apiFetch<PagedResult<AgentExecutionSummary>>(`/agent-executions?${query.toString()}`)
}

export const getAgentExecutionSteps = (executionId: string) => apiFetch<AgentExecutionStep[]>(`/agent-executions/${executionId}/steps`)

export interface AgentToolCall {
  id: string
  agentExecutionStepId: string
  toolName: string
  riskLevel: string
  requiredPermissionsJson: string
  validatedInputJson: string
  validatedOutputJson: string | null
  failureReason: string | null
  wasApprovalRequired: boolean
  startedAtUtc: string | null
  completedAtUtc: string | null
}

export const getAgentToolCalls = (executionId: string) => apiFetch<AgentToolCall[]>(`/agent-executions/${executionId}/tool-calls`)

export interface AgentExecutionUsage {
  inputTokenCount: number | null
  outputTokenCount: number | null
  reasoningTokenCount: number | null
  toolCallCount: number
  stepCount: number
  estimatedCost: number | null
  costCurrency: string | null
}

export const getAgentExecutionUsage = (executionId: string) => apiFetch<AgentExecutionUsage>(`/agent-executions/${executionId}/usage`)
