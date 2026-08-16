import { apiFetch } from '../../../api/httpClient'
import type { WorkflowNodeType } from './workflowsApi'

/** Administrator-managed auto-approval rule for the workflow engine's platform-mandatory approval baseline (spec.md "Approval Policies") — mirrors `WorkflowPolicyDto`. */
export interface WorkflowPolicy {
  id: string
  name: string
  description: string | null
  workflowNodeType: WorkflowNodeType | null
  underlyingToolName: string | null
  conditionsJson: string | null
  createdByUserId: string
  isEnabled: boolean
  createdAtUtc: string
  modifiedAtUtc: string | null
}

export interface SaveWorkflowPolicyInput {
  name: string
  description: string | null
  workflowNodeType: WorkflowNodeType | null
  underlyingToolName: string | null
  conditionsJson: string | null
}

export interface UpdateWorkflowPolicyInput {
  name: string
  description: string | null
  conditionsJson: string | null
  isEnabled: boolean
}

export const listWorkflowPolicies = () => apiFetch<WorkflowPolicy[]>('/admin/workflow-policies')

export const createWorkflowPolicy = (input: SaveWorkflowPolicyInput) =>
  apiFetch<WorkflowPolicy>('/admin/workflow-policies', { method: 'POST', body: JSON.stringify(input) })

export const updateWorkflowPolicy = (id: string, input: UpdateWorkflowPolicyInput) =>
  apiFetch<WorkflowPolicy>(`/admin/workflow-policies/${id}`, { method: 'PUT', body: JSON.stringify(input) })

export const deleteWorkflowPolicy = (id: string) => apiFetch<void>(`/admin/workflow-policies/${id}`, { method: 'DELETE' })
