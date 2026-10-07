import { apiFetch } from '../../../api/httpClient'

// specs/067 US8, contracts/admin-notifications-api.md "Localization settings".

export interface AvailableLanguage {
  code: string
  nativeName: string
  /** English is always supported and cannot be deselected (FR-044a). */
  locked: boolean
}

export interface LocalizationSettings {
  isEnabled: boolean
  supportedLanguages: string[]
  /** Only the languages the platform ships content for. */
  availableLanguages: AvailableLanguage[]
  /** Base64; goes back verbatim as `If-Match` on the PUT. */
  rowVersion: string
}

export interface LocalizationSettingsInput {
  isEnabled: boolean
  supportedLanguages: string[]
}

export const ADMIN_LOCALIZATION_QUERY_KEY = ['admin', 'localization'] as const

export const getLocalizationSettings = () => apiFetch<LocalizationSettings>('/admin/localization')

export const updateLocalizationSettings = (rowVersion: string, input: LocalizationSettingsInput) =>
  apiFetch<LocalizationSettings>('/admin/localization', {
    method: 'PUT',
    headers: { 'If-Match': rowVersion },
    body: JSON.stringify(input),
  })
