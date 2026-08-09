import { useQuery } from '@tanstack/react-query'
import * as memoryApi from '../api/memoryApi'

/** Lazily fetches the "why does Lucy know this" trace for one assistant message (spec.md FR-014) — disabled until both ids are known, and cached per message since a memory's usage on a given past response never changes. */
export function useMemoryReferences(chatId: string | null | undefined, messageId: string | null | undefined, enabled: boolean) {
  return useQuery({
    queryKey: ['memory-references', chatId, messageId],
    queryFn: () => memoryApi.getMemoryReferences(chatId!, messageId!),
    enabled: enabled && Boolean(chatId) && Boolean(messageId),
    staleTime: Infinity,
  })
}
