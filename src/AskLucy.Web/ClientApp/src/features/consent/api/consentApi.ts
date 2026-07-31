import { apiFetch } from '../../../api/httpClient'

export interface CookieConsentStatus {
  hasConsented: boolean
  requiresReconsent: boolean
  policyVersion: string | null
  currentPolicyVersion: string
  essential: boolean
  functional: boolean
  analytics: boolean
  marketing: boolean
  lastUpdatedAtUtc: string | null
}

export interface SaveCookieConsentInput {
  functional: boolean
  analytics: boolean
  marketing: boolean
}

export const getMyCookieConsent = () => apiFetch<CookieConsentStatus>('/users/me/cookie-consent')

export const saveMyCookieConsent = (input: SaveCookieConsentInput) =>
  apiFetch<CookieConsentStatus>('/users/me/cookie-consent', { method: 'PUT', body: JSON.stringify(input) })
