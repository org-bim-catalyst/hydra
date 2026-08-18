import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import * as promptsApi from '../api/promptsApi'
import type { ListPromptsParams } from '../api/promptsApi'

export const PROMPTS_QUERY_KEY = ['prompts']

export function usePrompt(id: string | null) {
  return useQuery({
    queryKey: [...PROMPTS_QUERY_KEY, id],
    queryFn: () => promptsApi.getPrompt(id!),
    enabled: id !== null,
  })
}

/** Cursor-paginated search/filter (FR-050–FR-053, User Story 4) — backs the virtualized Prompt Library list. */
export function useSearchPrompts(params: Omit<ListPromptsParams, 'cursor'>) {
  return useInfiniteQuery({
    queryKey: [...PROMPTS_QUERY_KEY, 'list', params],
    queryFn: ({ pageParam }: { pageParam: string | undefined }) => promptsApi.listPrompts({ ...params, cursor: pageParam }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  })
}
