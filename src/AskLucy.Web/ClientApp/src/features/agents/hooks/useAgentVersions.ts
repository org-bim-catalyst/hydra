import { useQuery } from '@tanstack/react-query'
import * as agentsApi from '../api/agentsApi'

export const AGENT_VERSIONS_QUERY_KEY = ['agent-versions']

/** spec.md User Story 6 — every published version of an agent, newest first. */
export function useAgentVersions(agentId: string | null) {
  return useQuery({
    queryKey: [...AGENT_VERSIONS_QUERY_KEY, agentId],
    queryFn: () => agentsApi.listAgentVersions(agentId!),
    enabled: agentId !== null,
  })
}
