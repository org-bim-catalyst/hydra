import { useQuery } from '@tanstack/react-query'
import * as documentsApi from '../api/documentsApi'
import type { DocumentListView, DocumentSearchFilters } from '../api/documentsApi'

export const DOCUMENTS_QUERY_KEY = ['documents']

export function useDocuments(
  view: DocumentListView = 'Active',
  folderId: string | null = null,
  filters: DocumentSearchFilters = {},
) {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, view, folderId, filters],
    queryFn: () => documentsApi.searchDocuments(view, folderId, null, 50, filters),
  })
}

export function useFolderTree() {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, 'folders', 'tree'],
    queryFn: () => documentsApi.getFolderTree(),
  })
}

export function useDocument(id: string | null) {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, id],
    queryFn: () => documentsApi.getDocument(id!),
    enabled: id !== null,
  })
}

/**
 * 5-second polling is a reconciliation fallback, not the primary update path — SignalR
 * (useDocumentProcessingHub) pushes changes immediately; this just guarantees a missed
 * push event self-heals within 5s (research.md Decision 7).
 */
export function useDocumentProcessingStatus(id: string | null) {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, id, 'processing'],
    queryFn: () => documentsApi.getDocumentProcessingStatus(id!),
    enabled: id !== null,
    refetchInterval: 5000,
  })
}

export function useProcessingHistory(id: string | null) {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, id, 'processing', 'history'],
    queryFn: () => documentsApi.getProcessingHistory(id!),
    enabled: id !== null,
  })
}

export function useVersionTimeline(documentId: string | null) {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, documentId, 'versions'],
    queryFn: () => documentsApi.getVersionTimeline(documentId!),
    enabled: documentId !== null,
  })
}

export function useCompareVersions(documentId: string | null, fromVersionId: string | null, toVersionId: string | null) {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, documentId, 'versions', 'compare', fromVersionId, toVersionId],
    queryFn: () => documentsApi.compareVersions(documentId!, fromVersionId!, toVersionId!),
    enabled: documentId !== null && fromVersionId !== null && toVersionId !== null,
  })
}

/** 5-second polling — same reconciliation-fallback rationale as useDocumentProcessingStatus (research.md Decision 7), reused here for the live dashboard counts. */
export function useDashboard() {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, 'dashboard'],
    queryFn: () => documentsApi.getDashboard(),
    refetchInterval: 5000,
  })
}

export function useOrganizationDashboard(enabled: boolean) {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, 'dashboard', 'organization'],
    queryFn: () => documentsApi.getOrganizationDashboard(),
    refetchInterval: 5000,
    enabled,
  })
}

export function useNotifications(unreadOnly = false) {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, 'notifications', unreadOnly],
    queryFn: () => documentsApi.getNotifications(unreadOnly),
  })
}

/** FR-043, FR-044 — the query handler itself returns `Unavailable` (never an error) for a document that hasn't reached the preview-generation stage yet. */
export function useDocumentPreview(documentId: string | null) {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, documentId, 'preview'],
    queryFn: () => documentsApi.getDocumentPreview(documentId!),
    enabled: documentId !== null,
  })
}

export function useTags() {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, 'tags'],
    queryFn: () => documentsApi.listTags(),
  })
}

export function useCategories() {
  return useQuery({
    queryKey: [...DOCUMENTS_QUERY_KEY, 'categories'],
    queryFn: () => documentsApi.listCategories(),
  })
}
