import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as consentApi from '../api/consentApi'
import type { SaveCookieConsentInput } from '../api/consentApi'

const COOKIE_CONSENT_QUERY_KEY = ['cookie-consent', 'me']

export function useCookieConsent() {
  return useQuery({ queryKey: COOKIE_CONSENT_QUERY_KEY, queryFn: consentApi.getMyCookieConsent })
}

export function useSaveCookieConsent() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: SaveCookieConsentInput) => consentApi.saveMyCookieConsent(input),
    // FR-014/SC-004: the new preferences must take effect immediately, without the user
    // reloading — seeding the cache with the response (rather than only invalidating and
    // waiting on a refetch) makes ConsentGate/CookiePreferencesPanel reflect the change on
    // the very next render.
    onSuccess: (status) => queryClient.setQueryData(COOKIE_CONSENT_QUERY_KEY, status),
  })
}
