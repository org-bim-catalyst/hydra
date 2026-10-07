import type { QueryClient } from '@tanstack/react-query'
import { http, HttpResponse } from 'msw'
import { vi } from 'vitest'
import { useAuthStore } from '../store/authStore'
import { useThemeStore, type ThemeMode } from '../store/themeStore'
import { LOCALIZATION_QUERY_KEY, type MyLocalization } from './useLocalization'

/** Shared setup for the Arabic/RTL screen tests (SC-011): an Arabic-preferring, signed-in user. */
export const arabicLocalization: MyLocalization = {
  localizationEnabled: true,
  supportedLanguages: [
    { code: 'en', nativeName: 'English' },
    { code: 'ar', nativeName: 'العربية' },
  ],
  preferredLanguage: 'ar',
  effectiveLanguage: 'ar',
  direction: 'rtl',
}

export const localizationHandler = (state: MyLocalization = arabicLocalization) =>
  http.get('*/api/v1/users/me/localization', () => HttpResponse.json(state))

/** Seeds the language state so a surface renders Arabic on its first render, without a signed-in session or a request. */
export const seedArabic = (queryClient: QueryClient) =>
  queryClient.setQueryData(LOCALIZATION_QUERY_KEY, arabicLocalization)

export const THEME_MODES: ThemeMode[] = ['light', 'dark']

export function signInWithTheme(mode: ThemeMode = 'light') {
  useAuthStore.setState({ accessToken: 'test-token', userId: 'user-1' })
  useThemeStore.setState({ mode })
}

/** For a surface seeded with `seedArabic`: no session, so the app shell's other requests and the hub stay quiet. */
export function setTheme(mode: ThemeMode = 'light') {
  useThemeStore.setState({ mode })
}

export function signOutAndResetTheme() {
  useAuthStore.setState({ accessToken: null, userId: null })
  useThemeStore.setState({ mode: 'light' })
}

/** Spies on `console.error` so a test can assert the dev-only "missing message" fallback never fired. */
export function watchI18nFallbacks() {
  const spy = vi.spyOn(console, 'error').mockImplementation(() => {})
  return {
    calls: () =>
      spy.mock.calls.filter(([first]) => typeof first === 'string' && first.startsWith('[i18n]')),
    restore: () => spy.mockRestore(),
  }
}

export const lacksArabicIndicDigits = (text: string) => !/[٠-٩۰-۹]/.test(text)
