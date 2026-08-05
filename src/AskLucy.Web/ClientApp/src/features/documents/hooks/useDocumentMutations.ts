import { useMutation, useQueryClient } from '@tanstack/react-query'
import * as documentsApi from '../api/documentsApi'
import type { OnContainedDocumentsAction, UpdateDocumentMetadataInput } from '../api/documentsApi'
import { DOCUMENTS_QUERY_KEY } from './useDocuments'

function useInvalidateDocuments() {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: DOCUMENTS_QUERY_KEY })
}

export function useRenameDocument() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: ({ id, fileName }: { id: string; fileName: string }) => documentsApi.renameDocument(id, fileName),
    onSuccess: invalidate,
  })
}

export function useArchiveDocument() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: (id: string) => documentsApi.archiveDocument(id),
    onSuccess: invalidate,
  })
}

export function useRestoreDocument() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: (id: string) => documentsApi.restoreDocument(id),
    onSuccess: invalidate,
  })
}

export function useDeleteDocument() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: (id: string) => documentsApi.deleteDocument(id),
    onSuccess: invalidate,
  })
}

export function useRetryProcessing() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: (id: string) => documentsApi.retryProcessing(id),
    onSuccess: invalidate,
  })
}

export function useUpdateDocumentMetadata() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateDocumentMetadataInput }) => documentsApi.updateDocumentMetadata(id, input),
    onSuccess: invalidate,
  })
}

export function useOverrideClassification() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: ({ id, categoryId }: { id: string; categoryId: string }) => documentsApi.overrideClassification(id, categoryId),
    onSuccess: invalidate,
  })
}

export function useAddTag() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: ({ id, name }: { id: string; name: string }) => documentsApi.addTag(id, name),
    onSuccess: invalidate,
  })
}

export function useRemoveTag() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: ({ id, name }: { id: string; name: string }) => documentsApi.removeTag(id, name),
    onSuccess: invalidate,
  })
}

export function useCreateFolder() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: ({ name, parentFolderId }: { name: string; parentFolderId: string | null }) => documentsApi.createFolder(name, parentFolderId),
    onSuccess: invalidate,
  })
}

export function useRenameFolder() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: ({ id, name }: { id: string; name: string }) => documentsApi.renameFolder(id, name),
    onSuccess: invalidate,
  })
}

export function useMoveFolder() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: ({ id, parentFolderId }: { id: string; parentFolderId: string | null }) => documentsApi.moveFolder(id, parentFolderId),
    onSuccess: invalidate,
  })
}

export function useDeleteFolder() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: ({ id, onContainedDocuments }: { id: string; onContainedDocuments?: OnContainedDocumentsAction }) =>
      documentsApi.deleteFolder(id, onContainedDocuments),
    onSuccess: invalidate,
  })
}

export function useMoveDocument() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: ({ id, folderId }: { id: string; folderId: string | null }) => documentsApi.moveDocument(id, folderId),
    onSuccess: invalidate,
  })
}

export function useDuplicateDocument() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: (id: string) => documentsApi.duplicateDocument(id),
    onSuccess: invalidate,
  })
}

export function useRestoreDocumentVersion() {
  const invalidate = useInvalidateDocuments()
  return useMutation({
    mutationFn: ({ documentId, versionId }: { documentId: string; versionId: string }) =>
      documentsApi.restoreDocumentVersion(documentId, versionId),
    onSuccess: invalidate,
  })
}

export function useMarkNotificationRead() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => documentsApi.markNotificationRead(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...DOCUMENTS_QUERY_KEY, 'notifications'] }),
  })
}
