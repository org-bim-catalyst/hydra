import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { getCookiePolicy } from '../../privacy/api/privacyApi'

const PUBLIC_CONSENT_COOKIE_NAME = 'flumeria_public_consent'
export const PUBLIC_CONSENT_QUERY_KEY = ['public-cookie-consent']

export interface PublicConsentState {
  hasConsented: boolean
  requiresReconsent: boolean
  policyVersion: string | null
  currentPolicyVersion: string
  essential: boolean
  functional: boolean
  analytics: boolean
  marketing: boolean
  decidedAtUtc: string | null
}

export interface SavePublicConsentInput {
  functional: boolean
  analytics: boolean
  marketing: boolean
}

interface StoredPublicConsent {
  policyVersion: string
  functional: boolean
  analytics: boolean
  marketing: boolean
  decidedAtUtc: string
}

function readStoredConsent(): StoredPublicConsent | null {
  const match = document.cookie.match(new RegExp(`(?:^|; )${PUBLIC_CONSENT_COOKIE_NAME}=([^;]*)`))
  if (!match) return null

  try {
    const parsed = JSON.parse(decodeURIComponent(match[1])) as Partial<StoredPublicConsent>
    if (!parsed.policyVersion || typeof parsed.decidedAtUtc !== 'string') return null
    return {
      policyVersion: parsed.policyVersion,
      functional: Boolean(parsed.functional),
      analytics: Boolean(parsed.analytics),
      marketing: Boolean(parsed.marketing),
      decidedAtUtc: parsed.decidedAtUtc,
    }
  } catch {
    // A malformed/tampered cookie is treated as "no decision yet," never as an error state
    // (data-model.md PublicConsentState validation rules) — the banner simply re-appears.
    return null
  }
}

function writeStoredConsent(value: StoredPublicConsent) {
  const maxAgeSeconds = 60 * 60 * 24 * 365 // ~1 year, mirrors typical consent-cookie practice
  document.cookie = `${PUBLIC_CONSENT_COOKIE_NAME}=${encodeURIComponent(JSON.stringify(value))}; path=/; max-age=${maxAgeSeconds}; SameSite=Lax`
}

async function loadPublicConsentState(): Promise<PublicConsentState> {
  const policy = await getCookiePolicy()
  const stored = readStoredConsent()

  if (!stored) {
    return {
      hasConsented: false,
      requiresReconsent: true,
      policyVersion: null,
      currentPolicyVersion: policy.version,
      essential: true,
      functional: false,
      analytics: false,
      marketing: false,
      decidedAtUtc: null,
    }
  }

  return {
    hasConsented: true,
    requiresReconsent: stored.policyVersion !== policy.version,
    policyVersion: stored.policyVersion,
    currentPolicyVersion: policy.version,
    essential: true,
    functional: stored.functional,
    analytics: stored.analytics,
    marketing: stored.marketing,
    decidedAtUtc: stored.decidedAtUtc,
  }
}

/**
 * Anonymous-safe companion to the authenticated `useCookieConsent` (research.md Topic 2):
 * the existing `/users/me/cookie-consent` endpoint is `[Authorize]`-only and 401s for
 * signed-out visitors, so this reads/writes a first-party cookie client-side instead of
 * calling it. Still reuses the platform's `COOKIE_CATEGORIES` taxonomy and the anonymous
 * `/cookie-policy` endpoint for the current policy version, for consistency with the
 * authenticated flow. Once a visitor signs in, the authenticated `ConsentGate` takes over
 * and this cookie is no longer consulted (data-model.md).
 */
export function usePublicCookieConsent() {
  return useQuery({ queryKey: PUBLIC_CONSENT_QUERY_KEY, queryFn: loadPublicConsentState })
}

/**
 * Imperative, call-time-fresh read of the current consent state — unlike reading a
 * component's already-rendered `usePublicCookieConsent()` value, this always resolves to
 * the query's settled data (using the cache if already fetched, awaiting an in-flight or
 * new fetch otherwise). `useFunnelAnalytics` uses this rather than a closed-over hook
 * value so a fast-resolving action (e.g. the OAuth completion exchange) can never race
 * ahead of the consent lookup and silently drop an event because the query merely hadn't
 * resolved yet at that instant.
 */
export function ensurePublicConsentState(queryClient: QueryClient) {
  return queryClient.ensureQueryData({ queryKey: PUBLIC_CONSENT_QUERY_KEY, queryFn: loadPublicConsentState })
}

export function useSavePublicCookieConsent() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (input: SavePublicConsentInput): Promise<PublicConsentState> => {
      const policy = await getCookiePolicy()
      const decidedAtUtc = new Date().toISOString()
      writeStoredConsent({ policyVersion: policy.version, ...input, decidedAtUtc })
      return {
        hasConsented: true,
        requiresReconsent: false,
        policyVersion: policy.version,
        currentPolicyVersion: policy.version,
        essential: true,
        ...input,
        decidedAtUtc,
      }
    },
    // Mirrors useSaveCookieConsent: seed the cache with the result so PublicConsentGate/
    // PublicConsentBanner reflect the change on the very next render, no refetch needed.
    onSuccess: (status) => queryClient.setQueryData(PUBLIC_CONSENT_QUERY_KEY, status),
  })
}
