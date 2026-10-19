import { apiFetch } from '../../../api/httpClient'

/** specs/082 — whether the effective window is the stored setting or the configured fallback. */
export type ProviderHealthRetentionSource = 'Stored' | 'Configuration'

export interface ProviderHealthRetention {
  retentionDays: number
  intervalMinutes: number
  source: ProviderHealthRetentionSource
  /** Base64; goes back verbatim as `If-Match` on the PUT. Null while the stored setting is missing. */
  rowVersion: string | null
}

export const PROVIDER_HEALTH_RETENTION_QUERY_KEY = ['admin', 'provider-health', 'retention'] as const

export function getProviderHealthRetention(): Promise<ProviderHealthRetention> {
  return apiFetch<ProviderHealthRetention>('/admin/provider-health/retention')
}

/** Guarded by the row version last read (specs/082); a stale one is a 409. */
export function updateProviderHealthRetention(
  rowVersion: string,
  retentionDays: number,
): Promise<ProviderHealthRetention> {
  return apiFetch<ProviderHealthRetention>('/admin/provider-health/retention', {
    method: 'PUT',
    headers: { 'If-Match': rowVersion },
    body: JSON.stringify({ retentionDays }),
  })
}
