import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as chatsApi from '../api/chatsApi'

const CHATS_QUERY_KEY = ['chats']

export function useChats() {
  return useQuery({ queryKey: CHATS_QUERY_KEY, queryFn: chatsApi.listChats })
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
