import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as retrievalApi from '../api/retrievalApi'

/** specs/016-rag-semantic-search US1 T058 — a conversation's attached knowledge bases, with a full-replace update. */
export function useConversationKnowledgeBases(chatId: string | null) {
  const queryClient = useQueryClient()

  const query = useQuery({
    queryKey: ['chats', chatId, 'knowledge-bases'],
    queryFn: () => retrievalApi.getConversationKnowledgeBases(chatId!),
    enabled: chatId !== null,
  })

  const mutation = useMutation({
    mutationFn: (knowledgeBaseIds: string[]) => retrievalApi.updateConversationKnowledgeBases(chatId!, knowledgeBaseIds),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['chats', chatId, 'knowledge-bases'] })
    },
  })

  return {
    knowledgeBaseIds: query.data?.knowledgeBaseIds ?? [],
    isLoading: query.isLoading,
    error: query.error,
    updateKnowledgeBases: mutation.mutateAsync,
    isUpdating: mutation.isPending,
  }
}
