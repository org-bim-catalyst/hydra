import { useInfiniteQuery } from '@tanstack/react-query'
import * as knowledgeBasesApi from '../api/knowledgeBasesApi'
import type { SearchKnowledgeBasesParams } from '../api/knowledgeBasesApi'
import { KNOWLEDGE_BASES_QUERY_KEY } from './useKnowledgeBases'

/** Cursor-paginated ("infinite scroll") knowledge base search/filter/sort (FR-022–FR-024, US4) — mirrors `useSearchChats`. */
export function useSearchKnowledgeBases(params: SearchKnowledgeBasesParams) {
  return useInfiniteQuery({
    queryKey: [...KNOWLEDGE_BASES_QUERY_KEY, 'search', params],
    queryFn: ({ pageParam }: { pageParam: string | undefined }) =>
      knowledgeBasesApi.searchKnowledgeBases({ ...params, cursor: pageParam }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  })
}
