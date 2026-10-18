import { apiFetch } from '../../../api/httpClient'

/** Read-only: how long provider health checks are kept (ProviderHealthCheck:RetentionDays). */
export interface ProviderHealthRetention {
  retentionDays: number
  intervalMinutes: number
}

export const PROVIDER_HEALTH_RETENTION_QUERY_KEY = ['admin', 'provider-health', 'retention'] as const

export function getProviderHealthRetention(): Promise<ProviderHealthRetention> {
  return apiFetch<ProviderHealthRetention>('/api/v1/admin/provider-health/retention')
}
