import { apiFetch } from '../../../api/httpClient'

export type WorkflowType = 'Manual' | 'EventDriven' | 'AgentAssisted' | 'Scheduled'
export type WorkflowStatus = 'Draft' | 'Published' | 'Archived' | 'Disabled' | 'Deprecated'

export type WorkflowNodeType =
  | 'Start'
  | 'End'
  | 'AiPrompt'
  | 'AiAgent'
  | 'RagSearch'
  | 'MemorySearch'
  | 'DocumentProcessing'
  | 'FileOperation'
  | 'McpTool'
  | 'NativeTool'
  | 'Transform'
  | 'Condition'
  | 'Parallel'
  | 'Merge'
  | 'HumanApproval'
  | 'Validation'
  | 'Delay'

export type WorkflowNodeApprovalPolicy = 'AlwaysRequire' | 'NeverRequire' | 'AboveRiskLevel' | 'ForThisNodeType'

export type WorkflowVariableKind = 'WorkflowVariable' | 'NodeOutputReference' | 'UserInput' | 'EnvironmentConfiguration' | 'SystemContext'

export type WorkflowVariableType = 'String' | 'Number' | 'Boolean' | 'Date' | 'Json' | 'Text' | 'File' | 'Document' | 'Collection'

export interface WorkflowDetail {
  id: string
  name: string
  description: string | null
  workflowType: WorkflowType
  status: WorkflowStatus
  draftDefinitionJson: string
  publishedVersionNumber: number | null
  eventTriggerConfigurationJson: string | null
  createdAtUtc: string
  modifiedAtUtc: string | null
}

export interface WorkflowListItem {
  id: string
  name: string
  description: string | null
  workflowType: WorkflowType
  status: WorkflowStatus
  publishedVersionNumber: number | null
  createdAtUtc: string
  modifiedAtUtc: string | null
}

export interface PagedResult<T> {
  items: T[]
  nextCursor: string | null
}

export interface WorkflowNode {
  id: string
  nodeKey: string
  nodeType: WorkflowNodeType
  name: string
  description: string | null
  configurationJson: string
  timeoutSeconds: number | null
  approvalPolicy: WorkflowNodeApprovalPolicy
  canvasX: number
  canvasY: number
}

export interface WorkflowConnection {
  id: string
  sourceNodeId: string
  targetNodeId: string
  branchLabel: string | null
}

export interface WorkflowVersion {
  id: string
  workflowId: string
  versionNumber: number
  inputsSchemaJson: string
  outputsSchemaJson: string
  executionPolicyJson: string
  changeDescription: string | null
  publishedBy: string
  createdAtUtc: string
  nodes: WorkflowNode[]
  connections: WorkflowConnection[]
}

/** One `WorkflowGraphValidator` finding (contracts/workflows-api.md's `ValidateWorkflowCommand`) — an empty array means the draft is publishable. */
export interface WorkflowValidationIssue {
  nodeKey: string | null
  message: string
}

export interface CreateWorkflowInput {
  name: string
  description: string | null
  workflowType: WorkflowType
  eventTriggerConfigurationJson?: string | null
}

export interface UpdateWorkflowInput {
  name: string
  description: string | null
  draftDefinitionJson: string
  eventTriggerConfigurationJson?: string | null
}

export const getWorkflow = (id: string) => apiFetch<WorkflowDetail>(`/workflows/${id}`)

export interface ListWorkflowsParams {
  status?: WorkflowStatus | null
  workflowType?: WorkflowType | null
  search?: string | null
  cursor?: string | null
  pageSize?: number
}

export const listWorkflows = (params: ListWorkflowsParams = {}) => {
  const query = new URLSearchParams()
  if (params.status) query.set('status', params.status)
  if (params.workflowType) query.set('workflowType', params.workflowType)
  if (params.search) query.set('search', params.search)
  if (params.cursor) query.set('cursor', params.cursor)
  if (params.pageSize) query.set('pageSize', String(params.pageSize))

  return apiFetch<PagedResult<WorkflowListItem>>(`/workflows?${query.toString()}`)
}

export const createWorkflow = (input: CreateWorkflowInput) =>
  apiFetch<WorkflowDetail>('/workflows', { method: 'POST', body: JSON.stringify(input) })

export const updateWorkflow = (id: string, input: UpdateWorkflowInput) =>
  apiFetch<WorkflowDetail>(`/workflows/${id}`, { method: 'PUT', body: JSON.stringify(input) })

export const validateWorkflow = (id: string) =>
  apiFetch<WorkflowValidationIssue[]>(`/workflows/${id}/actions/validate`, { method: 'POST' })

export const publishWorkflowVersion = (id: string, changeDescription: string | null) =>
  apiFetch<WorkflowVersion>(`/workflows/${id}/versions`, { method: 'POST', body: JSON.stringify({ changeDescription }) })

export const getWorkflowVersion = (id: string, versionNumber: number) =>
  apiFetch<WorkflowVersion>(`/workflows/${id}/versions/${versionNumber}`)

export const listWorkflowVersions = (id: string) => apiFetch<WorkflowVersion[]>(`/workflows/${id}/versions`)

export const duplicateWorkflow = (id: string) => apiFetch<WorkflowDetail>(`/workflows/${id}/actions/duplicate`, { method: 'POST' })

export const archiveWorkflow = (id: string) => apiFetch<WorkflowDetail>(`/workflows/${id}/actions/archive`, { method: 'POST' })

export const restoreWorkflow = (id: string) => apiFetch<WorkflowDetail>(`/workflows/${id}/actions/restore`, { method: 'POST' })

export const deleteWorkflow = (id: string) => apiFetch<void>(`/workflows/${id}`, { method: 'DELETE' })

/** FR-002 — stops event-trigger dispatch (Acceptance Scenario 9.3); manual starts remain allowed. */
export const disableWorkflow = (id: string) => apiFetch<WorkflowDetail>(`/workflows/${id}/actions/disable`, { method: 'POST' })

export const enableWorkflow = (id: string) => apiFetch<WorkflowDetail>(`/workflows/${id}/actions/enable`, { method: 'POST' })

/** FR-002 — a one-way lifecycle stage; no new manual or event-triggered executions start afterward. */
export const deprecateWorkflow = (id: string) => apiFetch<WorkflowDetail>(`/workflows/${id}/actions/deprecate`, { method: 'POST' })

/** Workflow Monitoring dashboard aggregate, scoped to the caller's own executions (spec.md User Story 8). */
export interface WorkflowStatistics {
  activeCount: number
  queuedCount: number
  failedCount: number
  completedCount: number
  averageDurationSeconds: number | null
  failureRate: number
  totalInputTokens: number
  totalOutputTokens: number
  totalEstimatedCost: number
}

export const getWorkflowStatistics = () => apiFetch<WorkflowStatistics>('/workflows/statistics')
