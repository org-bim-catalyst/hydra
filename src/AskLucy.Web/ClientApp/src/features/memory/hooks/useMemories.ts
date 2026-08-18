import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import * as memoryApi from '../api/memoryApi'
import type { ListMemoriesParams } from '../api/memoryApi'

const MEMORIES_QUERY_KEY = ['memories']

/** Cursor-paginated ("infinite scroll") Memory Center list/search/filter (spec.md FR-017, FR-018). */
export function useMemories(params: ListMemoriesParams) {
  return useInfiniteQuery({
    queryKey: [...MEMORIES_QUERY_KEY, 'list', params],
    queryFn: ({ pageParam }: { pageParam: string | undefined }) => memoryApi.listMemories({ ...params, cursor: pageParam }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  })
}

/** One memory's detail + edit history (spec.md FR-009, FR-019, User Story 2 AC2). */
export function useMemory(id: string | null) {
  return useQuery({
    queryKey: [...MEMORIES_QUERY_KEY, id],
    queryFn: () => memoryApi.getMemory(id!),
    enabled: id !== null,
  })
}

/** Pending-approval candidates only — the Memory Approval Queue (spec.md FR-021, User Story 3 AC1/AC2/AC3). */
export function usePendingMemories() {
  return useInfiniteQuery({
    queryKey: [...MEMORIES_QUERY_KEY, 'pending'],
    queryFn: ({ pageParam }: { pageParam: string | undefined }) =>
      memoryApi.listMemories({ state: 'PendingApproval', cursor: pageParam, pageSize: 50 }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  })
}

/** spec.md FR-007, FR-022, FR-025. */
export function useMemoryPreferences() {
  return useQuery({
    queryKey: [...MEMORIES_QUERY_KEY, 'preferences'],
    queryFn: () => memoryApi.getMemoryPreferences(),
  })
}

/** FR-006a — the poll fallback for anything missed while `useMemoryNotificationsHub`'s SignalR connection was down (same reconciliation principle as the document-processing notification hub). */
export function useMemoryNotifications() {
  return useQuery({
    queryKey: [...MEMORIES_QUERY_KEY, 'notifications'],
    queryFn: () => memoryApi.listMemoryNotifications(),
    refetchInterval: 30_000,
  })
}

/** spec.md FR-024, User Story 4 AC3 — polls until the background export job leaves `Processing` (a signed `downloadUrl` accompanies `Ready`). */
export function useMemoryExportStatus(exportJobId: string | null) {
  return useQuery({
    queryKey: [...MEMORIES_QUERY_KEY, 'export', exportJobId],
    queryFn: () => memoryApi.getMemoryExportStatus(exportJobId!),
    enabled: exportJobId !== null,
    refetchInterval: (query) => (query.state.data?.status === 'Processing' ? 2_000 : false),
  })
}

export { MEMORIES_QUERY_KEY }
