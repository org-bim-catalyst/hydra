import { useMutation, useQueryClient } from '@tanstack/react-query'
import * as memoryApi from '../api/memoryApi'
import { MEMORIES_QUERY_KEY } from './useMemories'

export function useEditMemory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, content }: { id: string; content: string }) => memoryApi.editMemory(id, content),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MEMORIES_QUERY_KEY }),
  })
}

export function useDeleteMemory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => memoryApi.deleteMemory(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MEMORIES_QUERY_KEY }),
  })
}

export function useApproveMemory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => memoryApi.approveMemory(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MEMORIES_QUERY_KEY }),
  })
}

export function useRejectMemory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => memoryApi.rejectMemory(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MEMORIES_QUERY_KEY }),
  })
}

/** spec.md FR-016, User Story 6 AC2/AC3. */
export function useResolveMemoryConflict() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, resolution }: { id: string; resolution: memoryApi.MemoryConflictResolution }) =>
      memoryApi.resolveMemoryConflict(id, resolution),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MEMORIES_QUERY_KEY }),
  })
}

export function useUpdateMemoryPreferences() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: Parameters<typeof memoryApi.updateMemoryPreferences>[0]) => memoryApi.updateMemoryPreferences(input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...MEMORIES_QUERY_KEY, 'preferences'] }),
  })
}

export function useMarkNotificationRead() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => memoryApi.markNotificationRead(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...MEMORIES_QUERY_KEY, 'notifications'] }),
  })
}

/** spec.md FR-023, User Story 4 AC2 — irreversible; the caller is expected to have already gated this behind an explicit confirmation dialog. */
export function useClearAllMemories() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => memoryApi.clearAllMemories(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MEMORIES_QUERY_KEY }),
  })
}

/** spec.md FR-024, User Story 4 AC3 — kicks off background generation; pair with `useMemoryExportStatus` to poll for the resulting `exportJobId`. */
export function useRequestMemoryExport() {
  return useMutation({
    mutationFn: () => memoryApi.requestMemoryExport(),
  })
}
