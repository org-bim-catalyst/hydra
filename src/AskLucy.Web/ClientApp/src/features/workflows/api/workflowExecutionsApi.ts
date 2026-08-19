import { apiFetch } from '../../../api/httpClient'

export type WorkflowExecutionStatus = 'Queued' | 'Running' | 'Paused' | 'WaitingForApproval' | 'Completed' | 'Failed' | 'Cancelled' | 'TimedOut'
export type WorkflowExecutionTriggerType = 'Manual' | 'EventDriven' | 'Test'
export type WorkflowExecutionNodeStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Skipped' | 'Cancelled' | 'WaitingForApproval'

export interface WorkflowExecutionNode {
  id: string
  workflowNodeId: string
  status: WorkflowExecutionNodeStatus
  outputJson: string | null
  retryCount: number
  skippedReason: string | null
  startedAtUtc: string | null
  completedAtUtc: string | null
}

export interface WorkflowError {
  id: string
  category: string
  message: string
  retryCount: number
  occurredAtUtc: string
}

export interface WorkflowApproval {
  id: string
  workflowExecutionNodeId: string
  intendedActionDescription: string
  parametersJson: string | null
  decision: 'Pending' | 'Approve' | 'Reject' | 'RequestChanges' | 'Cancel'
  wasPolicyBased: boolean
  decidedByUserId: string | null
  decidedAtUtc: string | null
}

export interface WorkflowExecutionSummary {
  id: string
  workflowId: string
  status: WorkflowExecutionStatus
  triggerType: WorkflowExecutionTriggerType
  createdAtUtc: string
}

export interface WorkflowExecutionDetail {
  id: string
  workflowId: string
  workflowVersionId: string
  status: WorkflowExecutionStatus
  triggerType: WorkflowExecutionTriggerType
  inputsJson: string
  finalOutputJson: string | null
  startedAtUtc: string | null
  completedAtUtc: string | null
  terminationReason: string | null
  nodes: WorkflowExecutionNode[]
  approvals: WorkflowApproval[]
  errors: WorkflowError[]
  inputTokenCount: number | null
  outputTokenCount: number | null
  estimatedCost: number | null
  createdAtUtc: string
}

export interface StartWorkflowExecutionInput {
  workflowId: string
  workflowVersionNumber: number | null
  inputsJson: string
  triggerType?: WorkflowExecutionTriggerType
}

export interface WorkflowExecutionEvent {
  id: string
  workflowNodeId: string | null
  eventType: string
  status: string
  safeMetadataJson: string | null
  occurredAtUtc: string
}

/** Never finishes synchronously (spec.md FR-047) — the run continues in the background; poll {@link getWorkflowExecution} for progress. */
export const startWorkflowExecution = (input: StartWorkflowExecutionInput) =>
  apiFetch<WorkflowExecutionSummary>('/workflow-executions', { method: 'POST', body: JSON.stringify(input) })

export const getWorkflowExecution = (id: string) => apiFetch<WorkflowExecutionDetail>(`/workflow-executions/${id}`)

export const getWorkflowApproval = (executionId: string, approvalId: string) =>
  apiFetch<WorkflowApproval>(`/workflow-executions/${executionId}/approvals/${approvalId}`)

export const approveWorkflowNode = (executionId: string, approvalId: string) =>
  apiFetch<WorkflowApproval>(`/workflow-executions/${executionId}/approvals/${approvalId}/approve`, { method: 'POST' })

export const rejectWorkflowNode = (executionId: string, approvalId: string, reason: string | null) =>
  apiFetch<WorkflowApproval>(`/workflow-executions/${executionId}/approvals/${approvalId}/reject`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  })

export const requestWorkflowNodeChanges = (executionId: string, approvalId: string, comments: string) =>
  apiFetch<WorkflowApproval>(`/workflow-executions/${executionId}/approvals/${approvalId}/request-changes`, {
    method: 'POST',
    body: JSON.stringify({ comments }),
  })

export const pauseWorkflowExecution = (executionId: string) =>
  apiFetch<void>(`/workflow-executions/${executionId}/pause`, { method: 'POST' })

export const resumeWorkflowExecution = (executionId: string) =>
  apiFetch<void>(`/workflow-executions/${executionId}/resume`, { method: 'POST' })

export const cancelWorkflowExecution = (executionId: string) =>
  apiFetch<void>(`/workflow-executions/${executionId}/cancel`, { method: 'POST' })

export const getWorkflowExecutionEvents = (executionId: string, since: string | null = null) => {
  const query = since ? `?since=${encodeURIComponent(since)}` : ''
  return apiFetch<WorkflowExecutionEvent[]>(`/workflow-executions/${executionId}/events${query}`)
}

export interface PagedResult<T> {
  items: T[]
  nextCursor: string | null
}

export interface ListWorkflowExecutionsParams {
  workflowId?: string | null
  status?: WorkflowExecutionStatus | null
  triggerType?: WorkflowExecutionTriggerType | null
  cursor?: string | null
  pageSize?: number
}

/** User Story 8 — cursor-paginated execution history, most recent first (spec.md FR-051/FR-050). */
export const listWorkflowExecutions = (params: ListWorkflowExecutionsParams = {}) => {
  const query = new URLSearchParams()
  if (params.workflowId) query.set('workflowId', params.workflowId)
  if (params.status) query.set('status', params.status)
  if (params.triggerType) query.set('triggerType', params.triggerType)
  if (params.cursor) query.set('cursor', params.cursor)
  if (params.pageSize) query.set('pageSize', String(params.pageSize))

  return apiFetch<PagedResult<WorkflowExecutionSummary>>(`/workflow-executions?${query.toString()}`)
}

export const getWorkflowExecutionNodes = (executionId: string) => apiFetch<WorkflowExecutionNode[]>(`/workflow-executions/${executionId}/nodes`)

export interface WorkflowExecutionUsage {
  inputTokenCount: number | null
  outputTokenCount: number | null
  reasoningTokenCount: number | null
  toolCallCount: number
  estimatedCost: number | null
  costCurrency: string | null
}

export const getWorkflowExecutionUsage = (executionId: string) => apiFetch<WorkflowExecutionUsage>(`/workflow-executions/${executionId}/usage`)
