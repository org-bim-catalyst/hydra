import { beforeEach, describe, expect, it, vi } from 'vitest'

const getPanelPreferencesMock = vi.fn()
const savePanelPreferencesMock = vi.fn()

vi.mock('../../../features/settings/api/panelPreferencesApi', () => ({
  getPanelPreferences: () => getPanelPreferencesMock(),
  savePanelPreferences: (preference: unknown) => savePanelPreferencesMock(preference),
}))

import { usePanelPreferencesStore } from './panelPreferencesStore'

const initialState = usePanelPreferencesStore.getState()

describe('panelPreferencesStore', () => {
  beforeEach(() => {
    usePanelPreferencesStore.setState(initialState, true)
    getPanelPreferencesMock.mockReset()
    savePanelPreferencesMock.mockReset()
  })

  it('defaults to 85% opacity before hydration', () => {
    expect(usePanelPreferencesStore.getState().opacityPercent).toBe(85)
  })

  it('hydrateFromServer applies the fetched preference', async () => {
    getPanelPreferencesMock.mockResolvedValue({ opacityPercent: 60 })

    await usePanelPreferencesStore.getState().hydrateFromServer()

    expect(usePanelPreferencesStore.getState().opacityPercent).toBe(60)
    expect(usePanelPreferencesStore.getState().error).toBeNull()
  })

  it('hydrateFromServer records an error without throwing, on failure', async () => {
    getPanelPreferencesMock.mockRejectedValue(new Error('network down'))

    await usePanelPreferencesStore.getState().hydrateFromServer()

    expect(usePanelPreferencesStore.getState().error).toBe('network down')
  })

  it('update optimistically applies the change, then confirms it from the server response', async () => {
    savePanelPreferencesMock.mockResolvedValue({ opacityPercent: 70 })

    const updatePromise = usePanelPreferencesStore.getState().update({ opacityPercent: 70 })
    expect(usePanelPreferencesStore.getState().opacityPercent).toBe(70) // optimistic
    await updatePromise

    expect(usePanelPreferencesStore.getState().opacityPercent).toBe(70)
    expect(savePanelPreferencesMock).toHaveBeenCalledWith({ opacityPercent: 70 })
  })

  it('update rolls back to the last-known-good value and records an error, on failure', async () => {
    savePanelPreferencesMock.mockRejectedValue(new Error('save failed'))

    await usePanelPreferencesStore.getState().update({ opacityPercent: 55 })

    expect(usePanelPreferencesStore.getState().opacityPercent).toBe(85) // rolled back
    expect(usePanelPreferencesStore.getState().error).toBe('save failed')
  })

  it('clearError resets the error field', () => {
    usePanelPreferencesStore.setState({ error: 'something went wrong' })

    usePanelPreferencesStore.getState().clearError()

    expect(usePanelPreferencesStore.getState().error).toBeNull()
  })
})
