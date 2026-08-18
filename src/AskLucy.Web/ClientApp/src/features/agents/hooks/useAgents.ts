import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import * as agentsApi from '../api/agentsApi'
import type { ListAgentsParams } from '../api/agentsApi'

export const AGENTS_QUERY_KEY = ['agents']

export function useAgent(id: string | null) {
  return useQuery({
    queryKey: [...AGENTS_QUERY_KEY, id],
    queryFn: () => agentsApi.getAgent(id!),
    enabled: id !== null,
  })
}

/** Cursor-paginated listing (spec.md User Story 1) — backs the Agent Library. */
export function useSearchAgents(params: Omit<ListAgentsParams, 'cursor'>) {
  return useInfiniteQuery({
    queryKey: [...AGENTS_QUERY_KEY, 'list', params],
    queryFn: ({ pageParam }: { pageParam: string | undefined }) => agentsApi.listAgents({ ...params, cursor: pageParam }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  })
}
