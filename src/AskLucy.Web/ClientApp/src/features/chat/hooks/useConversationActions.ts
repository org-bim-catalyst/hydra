import { useMutation, useQueryClient, type QueryKey } from '@tanstack/react-query'
import * as chatsApi from '../api/chatsApi'
import type { ConversationSummary, PagedResult } from '../api/chatsApi'

const CHATS_QUERY_KEY = ['chats']

type InfinitePages = { pages: PagedResult<ConversationSummary>[]; pageParams: unknown[] }

/**
 * Optimistically patches every cached conversation-search page containing `chatId`
 * (research.md Topic 9) — TanStack Query's standard onMutate/onError rollback pattern, so a
 * pin/favorite/archive/etc. action is reflected in the UI immediately (SC-005) and reverted
 * with a visible error (via the caller's Snackbar) if the request fails (constitution §2.VIII
 * No Silent Failures).
 */
function useOptimisticConversationMutation(
  mutationFn: (chatId: string) => Promise<ConversationSummary | void>,
  patch: (chat: ConversationSummary) => ConversationSummary,
) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onMutate: async (chatId: string) => {
      await queryClient.cancelQueries({ queryKey: CHATS_QUERY_KEY })
      const previous = queryClient.getQueriesData<InfinitePages>({ queryKey: CHATS_QUERY_KEY })

      previous.forEach(([key, data]) => {
        if (!data) return
        queryClient.setQueryData<InfinitePages>(key as QueryKey, {
          ...data,
          pages: data.pages.map((page) => ({
            ...page,
            items: page.items.map((item) => (item.id === chatId ? patch(item) : item)),
          })),
        })
      })

      return { previous }
    },
    onError: (_err, _chatId, context) => {
      context?.previous.forEach(([key, data]) => {
        queryClient.setQueryData(key, data)
      })
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: CHATS_QUERY_KEY })
    },
  })
}

export function useArchiveChat() {
  return useOptimisticConversationMutation(chatsApi.archiveChat, (c) => ({ ...c, isArchived: true }))
}

export function useRestoreChat() {
  return useOptimisticConversationMutation(chatsApi.restoreChat, (c) => ({ ...c, isArchived: false, isDeleted: false }))
}

export function usePinChat() {
  return useOptimisticConversationMutation(chatsApi.pinChat, (c) => ({ ...c, isPinned: true }))
}

export function useUnpinChat() {
  return useOptimisticConversationMutation(chatsApi.unpinChat, (c) => ({ ...c, isPinned: false }))
}

export function useFavoriteChat() {
  return useOptimisticConversationMutation(chatsApi.favoriteChat, (c) => ({ ...c, isFavorite: true }))
}

export function useUnfavoriteChat() {
  return useOptimisticConversationMutation(chatsApi.unfavoriteChat, (c) => ({ ...c, isFavorite: false }))
}

export function useDuplicateChat() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: chatsApi.duplicateChat,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CHATS_QUERY_KEY }),
  })
}

export function useClearChatMessages() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: chatsApi.clearChatMessages,
    onSuccess: (_data, chatId) => {
      void queryClient.invalidateQueries({ queryKey: ['chats', chatId, 'messages'] })
    },
  })
}

export function usePurgeChat() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: chatsApi.purgeChat,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CHATS_QUERY_KEY }),
  })
}
