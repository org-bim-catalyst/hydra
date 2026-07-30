import { apiFetch } from '../../../api/httpClient'

export interface CookiePolicy {
  version: string
  effectiveAtUtc: string
}

export const getCookiePolicy = () => apiFetch<CookiePolicy>('/cookie-policy')
