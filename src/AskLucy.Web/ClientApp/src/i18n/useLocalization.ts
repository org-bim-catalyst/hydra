import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '../api/httpClient'
import { useAuthStore } from '../store/authStore'
import { DEFAULT_LANGUAGE, DIRECTION_OF, isLanguage, type Direction, type Language } from './types'

// contracts/notifications-api.md — GET/PUT /users/me/localization.

export interface SupportedLanguage {
  code: string
  nativeName: string
}

export interface MyLocalization {
  localizationEnabled: boolean
  supportedLanguages: SupportedLanguage[]
  preferredLanguage: string | null
  effectiveLanguage: string
  direction: Direction
}

export const LOCALIZATION_QUERY_KEY = ['localization'] as const
export const SET_LANGUAGE_MUTATION_KEY = [...LOCALIZATION_QUERY_KEY, 'set'] as const

export const getMyLocalization = () => apiFetch<MyLocalization>('/users/me/localization')

export const setMyLanguage = (preferredLanguage: string) =>
  apiFetch<MyLocalization>('/users/me/localization', {
    method: 'PUT',
    body: JSON.stringify({ preferredLanguage }),
  })

/**
 * The caller's language state. Only fetched for a signed-in caller (the endpoint is authenticated). While it is
 * loading, failed, or absent, the app renders English — the documented default (FR-044) — rather than blocking.
 * A failure of the switch itself is surfaced by its caller (`LanguageSwitch`).
 */
export function useLocalization() {
  const signedIn = useAuthStore((state) => state.accessToken !== null)
  return useQuery({
    queryKey: LOCALIZATION_QUERY_KEY,
    queryFn: getMyLocalization,
    enabled: signedIn,
    staleTime: 5 * 60_000,
  })
}

/** Changes the caller's language and invalidates `['localization']`, so every `LocalizedSurface` re-renders without a reload. */
export function useSetLanguage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationKey: SET_LANGUAGE_MUTATION_KEY,
    mutationFn: setMyLanguage,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: LOCALIZATION_QUERY_KEY }),
  })
}

/** The switch is offered only when localization is on and there is something to switch between (FR-044b). */
export const canSwitchLanguage = (state: MyLocalization | undefined): state is MyLocalization =>
  state !== undefined && state.localizationEnabled && state.supportedLanguages.length > 1

export interface EffectiveLocalization {
  language: Language
  direction: Direction
  /** False for English or while localization is off: nothing is mounted and the English rendering is untouched. */
  isLocalized: boolean
}

export function useEffectiveLocalization(): EffectiveLocalization {
  const { data } = useLocalization()
  if (
    !data?.localizationEnabled ||
    !isLanguage(data.effectiveLanguage) ||
    data.effectiveLanguage === DEFAULT_LANGUAGE
  ) {
    return {
      language: DEFAULT_LANGUAGE,
      direction: DIRECTION_OF[DEFAULT_LANGUAGE],
      isLocalized: false,
    }
  }
  return {
    language: data.effectiveLanguage,
    direction: data.direction ?? DIRECTION_OF[data.effectiveLanguage],
    isLocalized: true,
  }
}
