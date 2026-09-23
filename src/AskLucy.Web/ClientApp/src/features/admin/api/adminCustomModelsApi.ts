import { apiFetch } from '../../../api/httpClient'

/** specs/072 contracts/admin-custom-models.md. Nothing here ever carries the deployment target's host, username, password or root path. */
export type DeploymentState = 'Queued' | 'Listing' | 'Transferring' | 'Completed' | 'Failed' | 'Cancelled'
export type Availability = 'Available' | 'Unavailable'
export type TransferPhase = 'Downloading' | 'Uploading' | 'Verifying'

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}

export interface CustomModelSummary {
  id: string
  name: string
  repositoryId: string
  revision: string
  resolvedCommitSha: string | null
  sourceUrl: string
  /** Relative to the deployment root only; never includes it. */
  destination: string
  deploymentState: DeploymentState
  availability: Availability
  canMakeAvailable: boolean
  availabilityBlockedReason: string | null
  canRemove: boolean
  canCancel: boolean
  totalBytes: number | null
  transferredBytes: number
  totalFileCount: number | null
  completedFileCount: number
  currentFilePath: string | null
  currentFileBytes: number | null
  currentFileTotalBytes: number | null
  overwrittenFileCount: number
  failureKind: string | null
  failureReason: string | null
  submittedBy: { id: string; displayName: string }
  createdAtUtc: string
  startedAtUtc: string | null
  finishedAtUtc: string | null
  /** e.g. "Supertonic" when the repository backs a hosted engine. */
  backsEngine: string | null
}

export interface OverwrittenFile {
  relativePath: string
  previousSizeBytes: number
  overwrittenAtUtc: string
}

export interface CustomModelDetail extends CustomModelSummary {
  overwrittenFiles: Paged<OverwrittenFile>
}

export interface DeploymentStatus {
  isConfigured: boolean
  /** 'FTP' only when plain FTP is explicitly allowed; shown as a warning. */
  transport: 'FTPS' | 'FTP' | null
  maxDeploymentBytes: number
  allowedDestinationPrefixes: string[]
}

export interface SourcePreview {
  isValid: boolean
  error: string | null
  repositoryId: string | null
  revision: string | null
  ignoredFilePath: string | null
  derivedName: string | null
  /** false → the dialog shows the required Name field. */
  nameAvailable: boolean
}

export interface SubmitCustomModelRequest {
  source: string
  destination: string
  name?: string
}

/** contracts/custom-model-deployments-hub.md `CustomModelDeploymentProgress`. */
export interface CustomModelDeploymentProgress {
  customModelId: string
  deploymentState: 'Listing' | 'Transferring'
  transferredBytes: number
  totalBytes: number
  completedFileCount: number
  totalFileCount: number
  currentFilePath: string | null
  currentFileBytes: number | null
  currentFileTotalBytes: number | null
  phase: TransferPhase
  overwrote: { relativePath: string; previousSizeBytes: number } | null
  sentAtUtc: string
}

/** `CustomModelDeploymentStateChanged` — the full summary, with `removed: true` after a delete. */
export type CustomModelDeploymentStateChanged = CustomModelSummary & { removed?: boolean }

export const CUSTOM_MODELS_QUERY_KEYS = {
  all: ['admin', 'custom-models'] as const,
  list: (page: number, pageSize: number) => ['admin', 'custom-models', 'list', page, pageSize] as const,
  detail: (id: string, overwrittenPage: number) => ['admin', 'custom-models', 'detail', id, overwrittenPage] as const,
  deploymentStatus: ['admin', 'custom-models', 'deployment-status'] as const,
}

const BASE = '/admin/custom-models'

export const getCustomModels = (page = 1, pageSize = 50) =>
  apiFetch<Paged<CustomModelSummary>>(`${BASE}?page=${page}&pageSize=${pageSize}`)

export const getCustomModel = (id: string, overwrittenPage = 1, overwrittenPageSize = 100) =>
  apiFetch<CustomModelDetail>(
    `${BASE}/${id}?overwrittenPage=${overwrittenPage}&overwrittenPageSize=${overwrittenPageSize}`,
  )

export const getDeploymentStatus = () => apiFetch<DeploymentStatus>(`${BASE}/deployment-status`)

/** Parses the URL on the server only; no outbound call. */
export const previewCustomModelSource = (source: string) =>
  apiFetch<SourcePreview>(`${BASE}/source-preview`, {
    method: 'POST',
    body: JSON.stringify({ source }),
  })

/** 202 once the record is saved and the job queued; never waits on a transfer. */
export const submitCustomModel = (request: SubmitCustomModelRequest) =>
  apiFetch<CustomModelSummary>(BASE, {
    method: 'POST',
    body: JSON.stringify(request),
  })

export const cancelCustomModelDeployment = (id: string) =>
  apiFetch<CustomModelSummary>(`${BASE}/${id}/actions/cancel`, { method: 'POST' })

export const setCustomModelAvailability = (id: string, availability: Availability) =>
  apiFetch<CustomModelSummary>(`${BASE}/${id}/availability`, {
    method: 'PUT',
    body: JSON.stringify({ availability }),
  })

/** Soft-deletes the record only; files on the deployment target are left as they are. */
export const removeCustomModel = (id: string) => apiFetch<void>(`${BASE}/${id}`, { method: 'DELETE' })
