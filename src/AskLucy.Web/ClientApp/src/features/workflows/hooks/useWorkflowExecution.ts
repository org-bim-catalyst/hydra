import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as workflowExecutionsApi from '../api/workflowExecutionsApi'
import type { StartWorkflowExecutionInput } from '../api/workflowExecutionsApi'

export const WORKFLOW_EXECUTIONS_QUERY_KEY = ['workflow-executions']

const IN_FLIGHT_STATUSES = ['Queued', 'Running', 'Paused', 'WaitingForApproval']

/**
 * Polls while the execution is still in flight (spec.md FR-047 — execution continues in the
 * background; this is the REST fallback path until the live hub arrives in User Story 6).
 */
export function useWorkflowExecution(id: string | null) {
  return useQuery({
    queryKey: [...WORKFLOW_EXECUTIONS_QUERY_KEY, id],
    queryFn: () => workflowExecutionsApi.getWorkflowExecution(id!),
    enabled: id !== null,
    refetchInterval: (query) => {
      const status = query.state.data?.status
      return status && IN_FLIGHT_STATUSES.includes(status) ? 2000 : false
    },
  })
}

export function useStartWorkflowExecution() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: StartWorkflowExecutionInput) => workflowExecutionsApi.startWorkflowExecution(input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: WORKFLOW_EXECUTIONS_QUERY_KEY }),
  })
}

/** spec.md User Story 5 — resumes the paused execution in the background once approved. */
export function useApproveWorkflowNode(executionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (approvalId: string) => workflowExecutionsApi.approveWorkflowNode(executionId, approvalId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...WORKFLOW_EXECUTIONS_QUERY_KEY, executionId] }),
  })
}

export function useRejectWorkflowNode(executionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ approvalId, reason }: { approvalId: string; reason: string | null }) =>
      workflowExecutionsApi.rejectWorkflowNode(executionId, approvalId, reason),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...WORKFLOW_EXECUTIONS_QUERY_KEY, executionId] }),
  })
}

export function useRequestWorkflowNodeChanges(executionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ approvalId, comments }: { approvalId: string; comments: string }) =>
      workflowExecutionsApi.requestWorkflowNodeChanges(executionId, approvalId, comments),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...WORKFLOW_EXECUTIONS_QUERY_KEY, executionId] }),
  })
}

/** spec.md User Story 6 — pause/resume/cancel a running execution. */
export function usePauseWorkflowExecution(executionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => workflowExecutionsApi.pauseWorkflowExecution(executionId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...WORKFLOW_EXECUTIONS_QUERY_KEY, executionId] }),
  })
}

export function useResumeWorkflowExecution(executionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => workflowExecutionsApi.resumeWorkflowExecution(executionId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...WORKFLOW_EXECUTIONS_QUERY_KEY, executionId] }),
  })
}

export function useCancelWorkflowExecution(executionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => workflowExecutionsApi.cancelWorkflowExecution(executionId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...WORKFLOW_EXECUTIONS_QUERY_KEY, executionId] }),
  })
}
