import { apiFetch } from '../../../api/httpClient'

/** specs/005-multi-provider-ai-engine contracts/providers.md */
export interface ProviderSummary {
  id: string
  providerKey: string
  displayName: string
  healthStatus: 'Unknown' | 'Healthy' | 'Unhealthy'
  healthStatusCheckedAtUtc: string | null
}

export interface ModelCapabilities {
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

export interface ModelPricing {
  inputPerMillionTokensUsd: number
  outputPerMillionTokensUsd: number
}

export interface ModelSummary {
  id: string
  modelKey: string
  displayName: string
  contextWindowTokens: number
  maxOutputTokens: number
  capabilities: ModelCapabilities
  pricing: ModelPricing | null
  releaseDate: string | null
  providerId: string
  providerDisplayName: string
}

export const getEnabledProviders = () => apiFetch<ProviderSummary[]>('/ai/providers')

export const getModelsForProvider = (providerId: string) =>
  apiFetch<ModelSummary[]>(`/ai/providers/${providerId}/models`)

export const getAllModels = () => apiFetch<ModelSummary[]>('/ai/models')
