import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as foldersApi from '../api/promptFoldersApi'
import { PROMPTS_QUERY_KEY } from './usePrompts'

const FOLDER_TREE_QUERY_KEY = ['prompt-folders']

export function useFolderTree() {
  return useQuery({
    queryKey: FOLDER_TREE_QUERY_KEY,
    queryFn: () => foldersApi.getFolderTree(),
  })
}

function useInvalidateFolderTree() {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: FOLDER_TREE_QUERY_KEY })
    // Prompts carry a denormalized FolderId — a move/delete can change which page a prompt appears on.
    void queryClient.invalidateQueries({ queryKey: PROMPTS_QUERY_KEY })
  }
}

export function useCreateFolder() {
  const invalidate = useInvalidateFolderTree()
  return useMutation({
    mutationFn: ({ name, parentFolderId }: { name: string; parentFolderId: string | null }) =>
      foldersApi.createFolder(name, parentFolderId),
    onSuccess: invalidate,
  })
}

export function useRenameFolder() {
  const invalidate = useInvalidateFolderTree()
  return useMutation({
    mutationFn: ({ id, name }: { id: string; name: string }) => foldersApi.renameFolder(id, name),
    onSuccess: invalidate,
  })
}

export function useMoveFolder() {
  const invalidate = useInvalidateFolderTree()
  return useMutation({
    mutationFn: ({ id, newParentFolderId }: { id: string; newParentFolderId: string | null }) =>
      foldersApi.moveFolder(id, newParentFolderId),
    onSuccess: invalidate,
  })
}

export function useDeleteFolder() {
  const invalidate = useInvalidateFolderTree()
  return useMutation({
    mutationFn: (id: string) => foldersApi.deleteFolder(id),
    onSuccess: invalidate,
  })
}
