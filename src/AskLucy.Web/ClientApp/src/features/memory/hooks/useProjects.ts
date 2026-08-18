import { useInfiniteQuery } from '@tanstack/react-query'
import * as projectsApi from '../api/projectsApi'

export const PROJECTS_QUERY_KEY = ['projects']

/** Cursor-paginated ("infinite scroll") Project list (spec.md FR-002a), newest-first. */
export function useProjects() {
  return useInfiniteQuery({
    queryKey: [...PROJECTS_QUERY_KEY, 'list'],
    queryFn: ({ pageParam }: { pageParam: string | undefined }) => projectsApi.listProjects(pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  })
}
