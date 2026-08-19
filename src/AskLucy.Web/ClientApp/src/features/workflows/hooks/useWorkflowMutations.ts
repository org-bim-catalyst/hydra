import { useMutation, useQueryClient } from '@tanstack/react-query'
import * as workflowsApi from '../api/workflowsApi'
import type { CreateWorkflowInput, UpdateWorkflowInput } from '../api/workflowsApi'
import { WORKFLOWS_QUERY_KEY } from './useWorkflows'

export function useCreateWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: CreateWorkflowInput) => workflowsApi.createWorkflow(input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY }),
  })
}

export function useUpdateWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateWorkflowInput }) => workflowsApi.updateWorkflow(id, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY }),
  })
}

/** spec.md FR-016 — before Publish is enabled, surface every `WorkflowGraphValidator` violation from the draft. */
export function useValidateWorkflow() {
  return useMutation({
    mutationFn: (id: string) => workflowsApi.validateWorkflow(id),
  })
}

export function usePublishWorkflowVersion() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, changeDescription }: { id: string; changeDescription: string | null }) =>
      workflowsApi.publishWorkflowVersion(id, changeDescription),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY }),
  })
}

/** spec.md User Story 3 — copies the current draft only, never version/execution history. */
export function useDuplicateWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => workflowsApi.duplicateWorkflow(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY }),
  })
}

export function useArchiveWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => workflowsApi.archiveWorkflow(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY }),
  })
}

export function useRestoreWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => workflowsApi.restoreWorkflow(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY }),
  })
}

export function useDeleteWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => workflowsApi.deleteWorkflow(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY }),
  })
}

/** spec.md FR-002 — stops event-trigger dispatch (Acceptance Scenario 9.3); manual starts remain allowed. */
export function useDisableWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => workflowsApi.disableWorkflow(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY }),
  })
}

export function useEnableWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => workflowsApi.enableWorkflow(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY }),
  })
}

/** spec.md FR-002 — a one-way lifecycle stage; no new manual or event-triggered executions start afterward. */
export function useDeprecateWorkflow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => workflowsApi.deprecateWorkflow(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: WORKFLOWS_QUERY_KEY }),
  })
}
