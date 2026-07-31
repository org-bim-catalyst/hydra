import { apiFetch } from '../../../api/httpClient'

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
}

export const getProviders = () => apiFetch<AdminAiProvider[]>('/admin/ai/providers')

export const updateProvider = (id: string, changes: { isEnabled?: boolean; defaultModelId?: string | null }) =>
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
  contextWindowTokens: number
  maxOutputTokens: number
  capabilities: AdminAiModelCapabilities
  pricing: AdminAiModelPricing | null
  releaseDate: string | null
  status: AdminAiModelStatus
}

/** Mirrors `ProviderModelInfo` — a model as reported by the vendor's own model-list API, not yet in the catalog. */
export interface AddedProviderModel {
  modelKey: string
  displayName: string
  contextWindowTokens: number
  maxOutputTokens: number
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
