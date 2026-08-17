import { apiFetch } from '../../../api/httpClient'

/** contracts/panel-preferences-api.md. */
export interface UserPanelPreference {
  opacityPercent: number
}

export const getPanelPreferences = () => apiFetch<UserPanelPreference>('/panels/preferences')

export const savePanelPreferences = (preference: UserPanelPreference) =>
  apiFetch<UserPanelPreference>('/panels/preferences', {
    method: 'PUT',
    body: JSON.stringify(preference),
  })
