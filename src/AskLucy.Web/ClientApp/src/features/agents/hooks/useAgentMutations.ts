import { useMutation, useQueryClient } from '@tanstack/react-query'
import * as agentsApi from '../api/agentsApi'
import type { SaveAgentInput } from '../api/agentsApi'
import { AGENTS_QUERY_KEY } from './useAgents'

export function useCreateAgent() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: SaveAgentInput) => agentsApi.createAgent(input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: AGENTS_QUERY_KEY }),
  })
}

export function useUpdateAgent() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: SaveAgentInput }) => agentsApi.updateAgent(id, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: AGENTS_QUERY_KEY }),
  })
}

export function usePublishAgentVersion() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, changeDescription }: { id: string; changeDescription: string | null }) =>
      agentsApi.publishAgentVersion(id, changeDescription),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: AGENTS_QUERY_KEY }),
  })
}

/** spec.md User Story 6 — copies the current draft only, never version/execution history. */
export function useDuplicateAgent() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => agentsApi.duplicateAgent(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: AGENTS_QUERY_KEY }),
  })
}

export function useArchiveAgent() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => agentsApi.archiveAgent(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: AGENTS_QUERY_KEY }),
  })
}

export function useRestoreAgent() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => agentsApi.restoreAgent(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: AGENTS_QUERY_KEY }),
  })
}

export function useDeleteAgent() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => agentsApi.deleteAgent(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: AGENTS_QUERY_KEY }),
  })
}
