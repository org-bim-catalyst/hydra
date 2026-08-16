import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import * as workflowsApi from '../api/workflowsApi'
import type { ListWorkflowsParams } from '../api/workflowsApi'

export const WORKFLOWS_QUERY_KEY = ['workflows']

export function useWorkflow(id: string | null) {
  return useQuery({
    queryKey: [...WORKFLOWS_QUERY_KEY, id],
    queryFn: () => workflowsApi.getWorkflow(id!),
    enabled: id !== null,
  })
}

/** Cursor-paginated listing (spec.md User Story 1) — backs the Workflow Library. */
export function useSearchWorkflows(params: Omit<ListWorkflowsParams, 'cursor'>) {
  return useInfiniteQuery({
    queryKey: [...WORKFLOWS_QUERY_KEY, 'list', params],
    queryFn: ({ pageParam }: { pageParam: string | undefined }) => workflowsApi.listWorkflows({ ...params, cursor: pageParam }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  })
}

export function useWorkflowVersion(id: string | null, versionNumber: number | null) {
  return useQuery({
    queryKey: [...WORKFLOWS_QUERY_KEY, id, 'versions', versionNumber],
    queryFn: () => workflowsApi.getWorkflowVersion(id!, versionNumber!),
    enabled: id !== null && versionNumber !== null,
  })
}
