import { apiFetch, API_BASE_URL } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'

export type DocumentFileType =
  | 'Pdf'
  | 'Word'
  | 'Excel'
  | 'PowerPoint'
  | 'Rtf'
  | 'Markdown'
  | 'Html'
  | 'Csv'
  | 'Json'
  | 'Xml'
  | 'Text'
  | 'Png'
  | 'Jpeg'
  | 'Tiff'
  | 'Bmp'
  | 'Webp'

export type DocumentProcessingStatus = 'Uploaded' | 'Queued' | 'Processing' | 'Completed' | 'Failed'

export type DocumentListView = 'Active' | 'Archived' | 'Deleted'

export interface DocumentSummary {
  id: string
  fileName: string
  fileType: DocumentFileType
  sizeBytes: number
  processingStatus: DocumentProcessingStatus
  folderId: string | null
  categoryName: string | null
  languagePrimary: string | null
  tags: string[]
  isArchived: boolean
  createdAtUtc: string
  lastUpdatedAtUtc: string | null
}

export interface DocumentMetadata {
  title: string | null
  author: string | null
  creationDate: string | null
  modificationDate: string | null
  keywords: string | null
  encoding: string | null
  isAutoExtracted: boolean
  rowVersion: string
}

export type DocumentLanguageRole = 'Primary' | 'Secondary'

export interface DocumentLanguage {
  languageCode: string
  role: DocumentLanguageRole
  confidenceScore: number
}

export type DocumentClassificationSource = 'Automatic' | 'UserOverride'

export interface DocumentClassification {
  categoryId: string
  categoryName: string
  source: DocumentClassificationSource
  confidenceScore: number | null
}

export interface DocumentDetail {
  summary: DocumentSummary
  originalFileName: string
  versionLabel: string
  rowVersion: string
  extractedText: string | null
  extractedStructure: string | null
  metadata: DocumentMetadata | null
  languages: DocumentLanguage[]
  classification: DocumentClassification | null
}

export interface UpdateDocumentMetadataResult {
  metadata: DocumentMetadata
  wasStale: boolean
}

export interface PagedResult<T> {
  items: T[]
  nextCursor: string | null
}

export interface StartUploadResult {
  uploadSessionId: string
  chunkSizeBytes: number
  expiresAtUtc: string
}

export interface UploadChunkResult {
  receivedChunkIndex: number
  nextExpectedChunkIndex: number
}

export interface CompleteUploadResult {
  isDuplicate: boolean
  duplicateOfDocumentId: string | null
  document: DocumentSummary | null
}

export interface SimpleUploadResult {
  isDuplicate: boolean
  duplicateOfDocumentId: string | null
  uploadSessionId: string | null
  document: DocumentSummary | null
}

export type DocumentProcessingStageType =
  | 'Validation'
  | 'Ocr'
  | 'TextExtraction'
  | 'MetadataExtraction'
  | 'Classification'
  | 'LanguageDetection'
  | 'PreviewGeneration'

export type DocumentProcessingStageStatus = 'Pending' | 'InProgress' | 'Completed' | 'Failed' | 'Skipped'

export interface DocumentProcessingStageDto {
  stageType: DocumentProcessingStageType
  status: DocumentProcessingStageStatus
  startedAtUtc: string | null
  completedAtUtc: string | null
}

export interface DocumentProcessingStatusDto {
  documentId: string
  processingStatus: DocumentProcessingStatus
  currentStage: DocumentProcessingStageType | null
  stages: DocumentProcessingStageDto[]
  failureReason: string | null
}

export interface DocumentProcessingLogDto {
  id: string
  eventType: string
  detail: string | null
  occurredAtUtc: string
}

export interface DocumentSearchFilters {
  q?: string
  author?: string
  language?: string
  tag?: string
  categoryId?: string
  dateFrom?: string
  dateTo?: string
  status?: DocumentProcessingStatus
}

export function searchDocuments(
  view: DocumentListView = 'Active',
  folderId: string | null = null,
  cursor: string | null = null,
  pageSize = 50,
  filters: DocumentSearchFilters = {},
): Promise<PagedResult<DocumentSummary>> {
  const params = new URLSearchParams({ view, pageSize: String(pageSize) })
  if (folderId) params.set('folderId', folderId)
  if (cursor) params.set('cursor', cursor)
  for (const [key, value] of Object.entries(filters)) {
    if (value) params.set(key, value)
  }
  return apiFetch(`/documents?${params.toString()}`)
}

export function getDocument(id: string): Promise<DocumentDetail> {
  return apiFetch(`/documents/${id}`)
}

export function renameDocument(id: string, fileName: string): Promise<DocumentSummary> {
  return apiFetch(`/documents/${id}`, { method: 'PATCH', body: JSON.stringify({ fileName }) })
}

export function archiveDocument(id: string): Promise<void> {
  return apiFetch(`/documents/${id}/actions/archive`, { method: 'POST' })
}

export function restoreDocument(id: string): Promise<void> {
  return apiFetch(`/documents/${id}/actions/restore`, { method: 'POST' })
}

export function deleteDocument(id: string): Promise<void> {
  return apiFetch(`/documents/${id}`, { method: 'DELETE' })
}

/**
 * The backend mints signed URLs via ASP.NET Core's `Url.Action` (e.g. `DocumentsController.Download`/
 * `.GetPreview`), which already returns a full app-rooted path including the `api/v1` segment
 * (`/api/v1/documents/versions/{id}/download-content?...`). `API_BASE_URL` itself also ends in
 * `/api/v1` (see `httpClient.ts`), so naively concatenating the two doubles that segment —
 * found while wiring up US7 preview image loading, which would have inherited the same bug from
 * `downloadDocument`. Stripping the trailing `/api/v1` first (mirroring `useDocumentProcessingHub`'s
 * existing hub-URL construction) combines the two correctly regardless of whether the SPA and API
 * share an origin.
 */
export function resolveSignedUrl(url: string): string {
  return `${API_BASE_URL.replace(/\/api\/v1$/, '')}${url}`
}

/** Fetches a signed download URL, then navigates the browser to it directly (not a redirect — see DocumentsController.Download's doc comment). */
export async function downloadDocument(id: string): Promise<void> {
  const { url } = await apiFetch<{ url: string; fileName: string }>(`/documents/${id}/download`)
  window.location.assign(resolveSignedUrl(url))
}

/** <paramref name="targetDocumentId"/> marks this as a US5 replace-version upload (contracts/document-versions-folders-api.md's "?documentId={id}") rather than a plain new-document upload. */
export function startUpload(fileName: string, sizeBytes: number, targetDocumentId: string | null = null): Promise<StartUploadResult> {
  const query = targetDocumentId ? `?documentId=${targetDocumentId}` : ''
  return apiFetch(`/documents/uploads${query}`, { method: 'POST', body: JSON.stringify({ fileName, sizeBytes }) })
}

/** Raw binary PUT — not JSON, so this bypasses apiFetch's Content-Type: application/json default (mirrors knowledgeBaseFoldersApi's multipart upload bypassing it for the same reason). */
export async function uploadChunk(uploadSessionId: string, chunkIndex: number, chunk: Blob): Promise<UploadChunkResult> {
  const accessToken = useAuthStore.getState().accessToken
  const response = await fetch(`${API_BASE_URL}/documents/uploads/${uploadSessionId}/chunks/${chunkIndex}`, {
    method: 'PUT',
    headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
    body: chunk,
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => undefined)
    throw new Error(problem?.detail ?? problem?.title ?? 'Chunk upload failed')
  }

  return response.json()
}

export function completeUpload(uploadSessionId: string): Promise<CompleteUploadResult> {
  return apiFetch(`/documents/uploads/${uploadSessionId}/complete`, { method: 'POST' })
}

export function completeUploadAsVersion(
  uploadSessionId: string,
  existingDocumentId: string,
  versionIncrement: 'Major' | 'Minor',
): Promise<DocumentSummary> {
  return apiFetch(`/documents/uploads/${uploadSessionId}/complete-as-version`, {
    method: 'POST',
    body: JSON.stringify({ existingDocumentId, versionIncrement }),
  })
}

export function completeUploadAsNew(uploadSessionId: string): Promise<DocumentSummary> {
  return apiFetch(`/documents/uploads/${uploadSessionId}/complete-as-new`, { method: 'POST' })
}

export function cancelUpload(uploadSessionId: string): Promise<void> {
  return apiFetch(`/documents/uploads/${uploadSessionId}`, { method: 'DELETE' })
}

export function getDocumentProcessingStatus(id: string): Promise<DocumentProcessingStatusDto> {
  return apiFetch(`/documents/${id}/processing`)
}

export function getProcessingHistory(id: string): Promise<DocumentProcessingLogDto[]> {
  return apiFetch(`/documents/${id}/processing/history`)
}

export function retryProcessing(id: string): Promise<void> {
  return apiFetch(`/documents/${id}/processing/actions/retry`, { method: 'POST' })
}

export interface UpdateDocumentMetadataInput {
  rowVersion: string
  title?: string | null
  author?: string | null
  creationDate?: string | null
  modificationDate?: string | null
  keywords?: string | null
}

export function updateDocumentMetadata(id: string, input: UpdateDocumentMetadataInput): Promise<UpdateDocumentMetadataResult> {
  return apiFetch(`/documents/${id}/metadata`, { method: 'PATCH', body: JSON.stringify(input) })
}

export function overrideClassification(id: string, categoryId: string): Promise<DocumentClassification> {
  return apiFetch(`/documents/${id}/classification`, { method: 'PUT', body: JSON.stringify({ categoryId }) })
}

export function listTags(): Promise<string[]> {
  return apiFetch('/documents/tags')
}

export function addTag(id: string, name: string): Promise<string[]> {
  return apiFetch(`/documents/${id}/tags`, { method: 'POST', body: JSON.stringify({ name }) })
}

export function removeTag(id: string, name: string): Promise<void> {
  return apiFetch(`/documents/${id}/tags/${encodeURIComponent(name)}`, { method: 'DELETE' })
}

export interface DocumentCategory {
  id: string
  name: string
  isSystemDefined: boolean
}

export function listCategories(): Promise<DocumentCategory[]> {
  return apiFetch('/documents/categories')
}

export interface DocumentFolder {
  id: string
  name: string
  parentFolderId: string | null
  depth: number
  documentCount: number
}

export function getFolderTree(): Promise<DocumentFolder[]> {
  return apiFetch('/documents/folders/tree')
}

export function createFolder(name: string, parentFolderId: string | null): Promise<DocumentFolder> {
  return apiFetch('/documents/folders', { method: 'POST', body: JSON.stringify({ name, parentFolderId }) })
}

export function renameFolder(id: string, name: string): Promise<DocumentFolder> {
  return apiFetch(`/documents/folders/${id}`, { method: 'PATCH', body: JSON.stringify({ name }) })
}

export function moveFolder(id: string, parentFolderId: string | null): Promise<DocumentFolder> {
  return apiFetch(`/documents/folders/${id}/parent`, { method: 'PATCH', body: JSON.stringify({ parentFolderId }) })
}

export type OnContainedDocumentsAction = 'MoveToParent' | 'ArchiveAll' | 'DeleteAll'

export function deleteFolder(id: string, onContainedDocuments?: OnContainedDocumentsAction): Promise<void> {
  const params = onContainedDocuments ? `?onContainedDocuments=${onContainedDocuments}` : ''
  return apiFetch(`/documents/folders/${id}${params}`, { method: 'DELETE' })
}

export function moveDocument(id: string, folderId: string | null): Promise<DocumentSummary> {
  return apiFetch(`/documents/${id}/folder`, { method: 'PATCH', body: JSON.stringify({ folderId }) })
}

export function duplicateDocument(id: string): Promise<DocumentSummary> {
  return apiFetch(`/documents/${id}/actions/duplicate`, { method: 'POST' })
}

export interface DocumentVersionSummary {
  id: string
  versionLabel: string
  sizeBytes: number
  createdAtUtc: string
  createdByUserId: string
  isCurrent: boolean
}

export interface MetadataFieldDiff {
  from: string | null
  to: string | null
}

export interface DocumentVersionCompare {
  extractedTextDiff: string
  metadataDiff: Record<string, MetadataFieldDiff>
}

export function getVersionTimeline(documentId: string): Promise<DocumentVersionSummary[]> {
  return apiFetch(`/documents/${documentId}/versions`)
}

export function compareVersions(documentId: string, fromVersionId: string, toVersionId: string): Promise<DocumentVersionCompare> {
  return apiFetch(`/documents/${documentId}/versions/compare?fromVersionId=${fromVersionId}&toVersionId=${toVersionId}`)
}

export function restoreDocumentVersion(documentId: string, versionId: string): Promise<DocumentSummary> {
  return apiFetch(`/documents/${documentId}/versions/${versionId}/actions/restore`, { method: 'POST' })
}

export function replaceDocument(documentId: string, uploadSessionId: string, versionIncrement: 'Major' | 'Minor'): Promise<DocumentSummary> {
  return apiFetch(`/documents/${documentId}/versions`, {
    method: 'POST',
    body: JSON.stringify({ uploadSessionId, versionIncrement }),
  })
}

export interface DocumentRetryQueueEntry {
  documentId: string
  fileName: string
  failureReason: string
}

export interface DocumentStatisticsSummary {
  totalDocuments: number
  totalStorageBytes: number
  averageProcessingDurationMs: number | null
  fileTypeDistribution: Record<string, number>
  languageDistribution: Record<string, number>
}

export interface DocumentDashboardSummary {
  queueDepth: number
  inProgressCount: number
  completedTodayCount: number
  failedCount: number
  retryQueue: DocumentRetryQueueEntry[]
  statistics: DocumentStatisticsSummary
}

export function getDashboard(): Promise<DocumentDashboardSummary> {
  return apiFetch('/documents/dashboard')
}

export function getOrganizationDashboard(): Promise<DocumentDashboardSummary> {
  return apiFetch('/documents/dashboard/organization')
}

export type DocumentNotificationEventType =
  | 'UploadCompleted'
  | 'ProcessingCompleted'
  | 'ProcessingFailed'
  | 'OcrFailed'
  | 'VersionCreated'
  | 'StorageLimitReached'

export interface DocumentNotificationDto {
  id: string
  documentId: string | null
  eventType: DocumentNotificationEventType
  message: string
  isRead: boolean
  createdAtUtc: string
}

export interface DocumentNotificationPage {
  items: DocumentNotificationDto[]
  nextCursor: string | null
}

export function getNotifications(unreadOnly = false, cursor: string | null = null, pageSize = 50): Promise<DocumentNotificationPage> {
  const params = new URLSearchParams({ unreadOnly: String(unreadOnly), pageSize: String(pageSize) })
  if (cursor) params.set('cursor', cursor)
  return apiFetch(`/documents/notifications?${params.toString()}`)
}

export function markNotificationRead(id: string): Promise<void> {
  return apiFetch(`/documents/notifications/${id}/actions/mark-read`, { method: 'POST' })
}

export type DocumentPreviewKind = 'PageImage' | 'Thumbnail' | 'StructuredContent' | 'Unavailable'

export interface DocumentPreview {
  previewType: DocumentPreviewKind
  url: string | null
  structuredContent: string | null
}

export function getDocumentPreview(id: string): Promise<DocumentPreview> {
  return apiFetch(`/documents/${id}/preview`)
}

/** multipart/form-data — small-file path (contracts/documents-api.md), same duplicate-detection/validation as the chunked flow. */
export async function simpleUpload(file: File): Promise<SimpleUploadResult> {
  const accessToken = useAuthStore.getState().accessToken
  const formData = new FormData()
  formData.append('file', file)

  const response = await fetch(`${API_BASE_URL}/documents/uploads/simple`, {
    method: 'POST',
    headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
    body: formData,
  })

  if (!response.ok && response.status !== 409) {
    const problem = await response.json().catch(() => undefined)
    throw new Error(problem?.detail ?? problem?.title ?? 'Upload failed')
  }

  return response.json()
}
