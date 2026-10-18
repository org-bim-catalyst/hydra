import { apiFetch } from '../../../api/httpClient'

/** specs/069 contracts §3 — counts only. */
export interface ModelDeprecationSummary {
  id: string
  modelId: string
  modelKey: string
  displayName: string
  providerId: string
  providerName: string
  replacementModelId: string | null
  replacementDisplayName: string | null
  validationStatus: 'AwaitingValidation' | 'Accepted' | 'Changed' | 'NotApplicable'
  reassignedCount: number
  openFlagCount: number
  userImpactStatus: 'Pending' | 'Completed'
  notifiedUserCount: number
  confirmedAtUtc: string
  confirmedByUserId: string
}

export interface PlatformDefaultChange {
  targetKind: 'ProviderDefault' | 'CapabilityAssignment'
  capability: string | null
  outcome: 'Reassigned' | 'Flagged'
  newModelId: string | null
  newModelDisplayName: string | null
  flagReason: string | null
  isOpenFlag: boolean
  resolvedAtUtc: string | null
}

export interface ModelDeprecationDetail extends ModelDeprecationSummary {
  batchId: string
  reason: string
  replacementSource: 'Proposed' | 'AdminChosen' | 'None'
  validatedByUserId: string | null
  validatedAtUtc: string | null
  platformDefaults: PlatformDefaultChange[]
}

export interface ModelDeprecationPage {
  items: ModelDeprecationSummary[]
  nextCursor: string | null
}

export const listModelDeprecations = (params: {
  providerId?: string
  awaitingValidation?: boolean
  openFlags?: boolean
  cursor?: string | null
  pageSize?: number
} = {}) => {
  const query = new URLSearchParams()
  if (params.providerId) query.set('providerId', params.providerId)
  if (params.awaitingValidation) query.set('awaitingValidation', 'true')
  if (params.openFlags) query.set('openFlags', 'true')
  if (params.cursor) query.set('cursor', params.cursor)
  query.set('pageSize', String(params.pageSize ?? 20))
  return apiFetch<ModelDeprecationPage>(`/admin/ai/deprecations?${query.toString()}`)
}

export const getModelDeprecation = (deprecationId: string) =>
  apiFetch<ModelDeprecationDetail>(`/admin/ai/deprecations/${deprecationId}`)

export const acceptModelReplacement = (deprecationId: string) =>
  apiFetch<void>(`/admin/ai/deprecations/${deprecationId}/actions/accept-replacement`, { method: 'POST' })

export const changeModelReplacement = (deprecationId: string, modelId: string) =>
  apiFetch<{ batchId: string }>(`/admin/ai/deprecations/${deprecationId}/actions/change-replacement`, {
    method: 'POST',
    body: JSON.stringify({ modelId }),
  })
