import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import * as chatsApi from '../api/chatsApi'
import type { SearchChatsParams } from '../api/chatsApi'

const CHATS_QUERY_KEY = ['chats']

/** Cursor-paginated ("infinite scroll") conversation search/filter/sort (FR-019–FR-022). */
export function useSearchChats(params: SearchChatsParams) {
  return useInfiniteQuery({
    queryKey: [...CHATS_QUERY_KEY, 'search', params],
    queryFn: ({ pageParam }: { pageParam: string | undefined }) => chatsApi.searchChats({ ...params, cursor: pageParam }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  })
}

export function useCreateChat() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (title: string) => chatsApi.createChat(title),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CHATS_QUERY_KEY }),
  })
}

export function useRenameChat() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, title }: { id: string; title: string }) => chatsApi.renameChat(id, title),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CHATS_QUERY_KEY }),
  })
}

export function useDeleteChat() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => chatsApi.deleteChat(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CHATS_QUERY_KEY }),
  })
}

/** Cursor-paginated ("infinite scroll") message history for one conversation (FR-022/FR-024). */
export function useChatMessages(chatId: string | null) {
  return useInfiniteQuery({
    queryKey: ['chats', chatId, 'messages'],
    queryFn: ({ pageParam }: { pageParam: string | undefined }) => chatsApi.getChatMessages(chatId!, pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    enabled: chatId !== null,
  })
}
