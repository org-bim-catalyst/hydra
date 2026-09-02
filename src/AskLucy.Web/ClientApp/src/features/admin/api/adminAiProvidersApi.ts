import { apiFetch } from '../../../api/httpClient'
import type { ProviderFailureKind } from '../../../api/httpClient'

/** specs/005-multi-provider-ai-engine — mirrors `AdminAiProviderDto`. Never includes the credential value itself. */
export interface AdminAiProvider {
  id: string
  providerKey: string
  displayName: string
  isEnabled: boolean
  hasCredential: boolean
  credentialLastRotatedAtUtc: string | null
  defaultModelId: string | null
  healthStatus: 'Unknown' | 'Healthy' | 'Unhealthy'
  healthStatusCheckedAtUtc: string | null
  /** specs/043 FR-016 — why the last check failed. Non-null only while `healthStatus` is `Unhealthy`. */
  healthFailureKind: ProviderFailureKind | null
  /** Administrator-facing prose for `healthFailureKind`. Never a raw vendor response body. */
  healthFailureReason: string | null
  /**
   * specs/043 FR-019 — the instant this result stops being trustworthy (`checkedAt + 3x the
   * configured check interval`). A horizon rather than a boolean, so an open page turns stale
   * on its own instead of showing a verdict frozen at render time.
   */
  healthStaleAfterUtc: string | null
}

/** specs/043 FR-024 — the result of an administrator-triggered probe. */
export interface CheckProviderHealthResult {
  healthStatus: AdminAiProvider['healthStatus']
  healthFailureKind: ProviderFailureKind | null
  healthFailureReason: string | null
  checkedAtUtc: string
  healthStaleAfterUtc: string | null
}

export const getProviders = () => apiFetch<AdminAiProvider[]>('/admin/ai/providers')

/** specs/043 FR-024. Bounded by the controller's existing `admin-endpoints` rate limit (FR-025). */
export const checkProviderHealth = (providerId: string) =>
  apiFetch<CheckProviderHealthResult>(`/admin/ai/providers/${providerId}/actions/check-health`, {
    method: 'POST',
  })

/**
 * `clearDefaultModel` rather than `defaultModelId: null`, because null already means "leave
 * it alone" server-side — a PATCH that only flips `isEnabled` must not wipe the default as a
 * side effect.
 */
export const updateProvider = (
  id: string,
  changes: { isEnabled?: boolean; defaultModelId?: string | null; clearDefaultModel?: boolean },
) =>
  apiFetch<void>(`/admin/ai/providers/${id}`, { method: 'PATCH', body: JSON.stringify(changes) })

export const setCredential = (id: string, apiKey: string) =>
  apiFetch<void>(`/admin/ai/providers/${id}/credential`, { method: 'PUT', body: JSON.stringify({ apiKey }) })

export const clearCredential = (id: string) =>
  apiFetch<void>(`/admin/ai/providers/${id}/credential`, { method: 'DELETE' })

export type AdminAiModelStatus = 'Available' | 'Deprecated' | 'Unavailable'

/** specs/008-ai-model-catalog-management — the admin view of one model, any status. Distinct from the chat feature's end-user `ModelSummary` (which omits `status` since end users only ever see `Available` models). */
export interface AdminAiModelCapabilities {
  streaming: boolean
  vision: boolean
  functionCalling: boolean
  jsonMode: boolean
  reasoning: boolean
  embeddings: boolean
  imageInput: boolean
  imageOutput: boolean
  audio: boolean
}

export interface AdminAiModelPricing {
  inputPerMillionTokensUsd: number
  outputPerMillionTokensUsd: number
}

export interface AdminAiModel {
  id: string
  modelKey: string
  displayName: string
  /** specs/043 FR-029/FR-030 — `null` means the vendor published no figure. Never rendered as 0. */
  contextWindowTokens: number | null
  maxOutputTokens: number | null
  capabilities: AdminAiModelCapabilities
  pricing: AdminAiModelPricing | null
  releaseDate: string | null
  status: AdminAiModelStatus
}

/** Mirrors `ProviderModelInfo` — a model as reported by the vendor's own model-list API, not yet in the catalog. */
export interface AddedProviderModel {
  modelKey: string
  displayName: string
  /** specs/043 FR-029 — `null` when the vendor publishes no token metadata (OpenAI publishes none at all). */
  contextWindowTokens: number | null
  maxOutputTokens: number | null
  capabilities: AdminAiModelCapabilities
}

/** A catalog model the vendor no longer lists. */
export interface RemovedProviderModel {
  id: string
  modelKey: string
  displayName: string
}

export interface ProviderModelSyncDiff {
  added: AddedProviderModel[]
  removedFromVendor: RemovedProviderModel[]
}

/** specs/009-selective-model-sync-review — a stale row skipped during a best-effort apply (FR-007a/FR-007b). */
export interface SyncApplyFailure {
  modelKey: string
  displayName: string
  reason: string
}

export interface ApplyProviderModelSyncResult {
  appliedModelKeys: string[]
  failed: SyncApplyFailure[]
}

export const getModels = (providerId: string) => apiFetch<AdminAiModel[]>(`/admin/ai/providers/${providerId}/models`)

export const updateModelStatus = (id: string, status: AdminAiModelStatus) =>
  apiFetch<void>(`/admin/ai/models/${id}`, { method: 'PATCH', body: JSON.stringify({ status }) })

export const syncModels = (providerId: string) =>
  apiFetch<ProviderModelSyncDiff>(`/admin/ai/providers/${providerId}/models/actions/sync`, { method: 'POST' })

export const applyModelSync = (providerId: string, diff: ProviderModelSyncDiff) =>
  apiFetch<ApplyProviderModelSyncResult>(`/admin/ai/providers/${providerId}/models/actions/sync/apply`, {
    method: 'POST',
    body: JSON.stringify(diff),
  })

/**
 * specs: the non-chat jobs the platform asks an LLM to do. Chat is absent on purpose — the
 * user's own Settings preference governs that, and these have no user to ask.
 */
export type AiCapability =
  | 'Chat'
  | 'LocationIntent'
  | 'MemoryExtraction'
  | 'MemoryConflictDetection'
  | 'DocumentClassification'
  | 'BoundaryVision'

export interface AiCapabilityAssignment {
  capability: AiCapability
  /** Null when nothing is assigned — the capability then falls back to the platform default. */
  providerId: string | null
  /** Where the capability actually lands today, assigned or not. Resolved server-side. */
  effectiveProviderId: string | null
  effectiveModelId: string | null
}

export const getCapabilityAssignments = () =>
  apiFetch<AiCapabilityAssignment[]>('/admin/ai/capabilities')

/** A null `providerId` clears the assignment, returning the capability to the platform default. */
export const setCapabilityAssignment = (capability: AiCapability, providerId: string | null) =>
  apiFetch<void>(`/admin/ai/capabilities/${capability}`, {
    method: 'PUT',
    body: JSON.stringify({ providerId }),
  })
