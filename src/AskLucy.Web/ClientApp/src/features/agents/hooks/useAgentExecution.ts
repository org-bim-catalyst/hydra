import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as agentExecutionsApi from '../api/agentExecutionsApi'
import type { StartAgentExecutionInput } from '../api/agentExecutionsApi'

export const AGENT_EXECUTIONS_QUERY_KEY = ['agent-executions']

/**
 * Polls while the execution is still in flight (spec.md FR-017 — execution continues in the
 * background; this is the REST fallback path until the live hub arrives in User Story 4).
 */
export function useAgentExecution(id: string | null) {
  return useQuery({
    queryKey: [...AGENT_EXECUTIONS_QUERY_KEY, id],
    queryFn: () => agentExecutionsApi.getAgentExecution(id!),
    enabled: id !== null,
    refetchInterval: (query) => {
      const status = query.state.data?.status
      return status && ['Queued', 'Running', 'Paused', 'WaitingForApproval'].includes(status) ? 2000 : false
    },
  })
}

export function useStartAgentExecution() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: StartAgentExecutionInput) => agentExecutionsApi.startAgentExecution(input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: AGENT_EXECUTIONS_QUERY_KEY }),
  })
}

/** spec.md User Story 3 — resumes the paused execution in the background once approved. */
export function useApproveAgentAction(executionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (approvalId: string) => agentExecutionsApi.approveAgentAction(executionId, approvalId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...AGENT_EXECUTIONS_QUERY_KEY, executionId] }),
  })
}

export function useRejectAgentAction(executionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ approvalId, reason }: { approvalId: string; reason: string | null }) =>
      agentExecutionsApi.rejectAgentAction(executionId, approvalId, reason),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...AGENT_EXECUTIONS_QUERY_KEY, executionId] }),
  })
}

/** spec.md User Story 4 — pause/resume/cancel a running execution. */
export function usePauseAgentExecution(executionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => agentExecutionsApi.pauseAgentExecution(executionId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...AGENT_EXECUTIONS_QUERY_KEY, executionId] }),
  })
}

export function useResumeAgentExecution(executionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => agentExecutionsApi.resumeAgentExecution(executionId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...AGENT_EXECUTIONS_QUERY_KEY, executionId] }),
  })
}

export function useCancelAgentExecution(executionId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => agentExecutionsApi.cancelAgentExecution(executionId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...AGENT_EXECUTIONS_QUERY_KEY, executionId] }),
  })
}
