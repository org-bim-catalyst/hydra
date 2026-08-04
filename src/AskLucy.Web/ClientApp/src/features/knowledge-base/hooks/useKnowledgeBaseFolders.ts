import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as foldersApi from '../api/knowledgeBaseFoldersApi'

const folderTreeKey = (knowledgeBaseId: string) => ['knowledge-bases', knowledgeBaseId, 'folder-tree']

export function useFolderTree(knowledgeBaseId: string) {
  return useQuery({
    queryKey: folderTreeKey(knowledgeBaseId),
    queryFn: () => foldersApi.getFolderTree(knowledgeBaseId),
  })
}

export function useDocuments(knowledgeBaseId: string, folderId: string | null) {
  return useQuery({
    queryKey: ['knowledge-bases', knowledgeBaseId, 'documents', folderId],
    queryFn: () => foldersApi.listDocuments(knowledgeBaseId, folderId),
  })
}

function useInvalidateFolderTree(knowledgeBaseId: string) {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: folderTreeKey(knowledgeBaseId) })
    void queryClient.invalidateQueries({ queryKey: ['knowledge-bases', knowledgeBaseId, 'documents'] })
    // Cached DocumentCount/StorageSizeBytes on the knowledge base itself change on upload/delete.
    void queryClient.invalidateQueries({ queryKey: ['knowledge-bases'] })
  }
}

export function useCreateFolder(knowledgeBaseId: string) {
  const invalidate = useInvalidateFolderTree(knowledgeBaseId)
  return useMutation({
    mutationFn: ({ name, parentFolderId }: { name: string; parentFolderId: string | null }) =>
      foldersApi.createFolder(knowledgeBaseId, name, parentFolderId),
    onSuccess: invalidate,
  })
}

export function useRenameFolder(knowledgeBaseId: string) {
  const invalidate = useInvalidateFolderTree(knowledgeBaseId)
  return useMutation({
    mutationFn: ({ folderId, name }: { folderId: string; name: string }) => foldersApi.renameFolder(knowledgeBaseId, folderId, name),
    onSuccess: invalidate,
  })
}

export function useMoveFolder(knowledgeBaseId: string) {
  const invalidate = useInvalidateFolderTree(knowledgeBaseId)
  return useMutation({
    mutationFn: ({ folderId, newParentFolderId }: { folderId: string; newParentFolderId: string | null }) =>
      foldersApi.moveFolder(knowledgeBaseId, folderId, newParentFolderId),
    onSuccess: invalidate,
  })
}

export function useDeleteFolder(knowledgeBaseId: string) {
  const invalidate = useInvalidateFolderTree(knowledgeBaseId)
  return useMutation({
    mutationFn: ({ folderId, confirm }: { folderId: string; confirm?: boolean }) =>
      foldersApi.deleteFolder(knowledgeBaseId, folderId, confirm),
    onSuccess: invalidate,
  })
}

export function useUploadDocument(knowledgeBaseId: string) {
  const invalidate = useInvalidateFolderTree(knowledgeBaseId)
  return useMutation({
    mutationFn: ({ file, folderId }: { file: File; folderId: string | null }) =>
      foldersApi.uploadDocument(knowledgeBaseId, file, folderId),
    onSuccess: invalidate,
  })
}

export function useMoveDocument(knowledgeBaseId: string) {
  const invalidate = useInvalidateFolderTree(knowledgeBaseId)
  return useMutation({
    mutationFn: ({ documentId, newFolderId }: { documentId: string; newFolderId: string | null }) =>
      foldersApi.moveDocument(knowledgeBaseId, documentId, newFolderId),
    onSuccess: invalidate,
  })
}

export function useDeleteDocument(knowledgeBaseId: string) {
  const invalidate = useInvalidateFolderTree(knowledgeBaseId)
  return useMutation({
    mutationFn: (documentId: string) => foldersApi.deleteDocument(knowledgeBaseId, documentId),
    onSuccess: invalidate,
  })
}
