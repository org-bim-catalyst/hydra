import { useMutation, useQueryClient } from '@tanstack/react-query'
import * as knowledgeBasesApi from '../api/knowledgeBasesApi'
import type { CreateKnowledgeBaseInput, UpdateKnowledgeBaseDetailsInput } from '../api/knowledgeBasesApi'
import { KNOWLEDGE_BASES_QUERY_KEY } from './useKnowledgeBases'

export function useCreateKnowledgeBase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: CreateKnowledgeBaseInput) => knowledgeBasesApi.createKnowledgeBase(input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY }),
  })
}

export function useUpdateKnowledgeBaseDetails() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateKnowledgeBaseDetailsInput }) =>
      knowledgeBasesApi.updateKnowledgeBaseDetails(id, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY }),
  })
}

/** Regular (soft) delete — no confirmation required, reversible via Restore (FR-005). */
export function useDeleteKnowledgeBase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => knowledgeBasesApi.deleteKnowledgeBase(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY }),
  })
}

/** Restores from soft-deleted (cancels the pending automatic purge, FR-036) or from Archived (US3) back to Active. */
export function useRestoreKnowledgeBase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => knowledgeBasesApi.restoreKnowledgeBase(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY }),
  })
}

/** Draft -> Active (research.md Decision 1). */
export function useActivateKnowledgeBase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => knowledgeBasesApi.activateKnowledgeBase(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY }),
  })
}

/** Active -> Archived (FR-004). */
export function useArchiveKnowledgeBase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => knowledgeBasesApi.archiveKnowledgeBase(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY }),
  })
}

/** Permanent delete (FR-036) — irreversible; the caller MUST show a confirmation dialog before invoking this (constitution §2.VIII — confirmation is also re-enforced server-side). */
export function usePurgeKnowledgeBase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => knowledgeBasesApi.purgeKnowledgeBase(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY }),
  })
}

/** FR-027/FR-028 — surfaces the knowledge base in the dashboard's Favorites section. */
export function useFavoriteKnowledgeBase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => knowledgeBasesApi.favoriteKnowledgeBase(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY }),
  })
}

export function useUnfavoriteKnowledgeBase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => knowledgeBasesApi.unfavoriteKnowledgeBase(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY }),
  })
}

/** FR-027/FR-028 — pinned knowledge bases sort first within every list/search result. */
export function usePinKnowledgeBase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => knowledgeBasesApi.pinKnowledgeBase(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY }),
  })
}

export function useUnpinKnowledgeBase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => knowledgeBasesApi.unpinKnowledgeBase(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY }),
  })
}

/** Deep copy (FR-032/FR-037) — the source knowledge base is unaffected; only the new list entry needs refetching. */
export function useDuplicateKnowledgeBase() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => knowledgeBasesApi.duplicateKnowledgeBase(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY }),
  })
}
