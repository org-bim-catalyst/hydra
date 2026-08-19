import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as panelPreferencesApi from '../api/panelPreferencesApi'
import { usePanelPreferencesStore } from '../../../viewer/panels/store/panelPreferencesStore'
import { ViewerTab } from './ViewerTab'

vi.mock('../api/panelPreferencesApi')

function resetStore() {
  usePanelPreferencesStore.setState({ opacityPercent: 85, error: null })
}

describe('ViewerTab', () => {
  beforeEach(() => {
    resetStore()
    vi.mocked(panelPreferencesApi.getPanelPreferences).mockResolvedValue({ opacityPercent: 85 })
    vi.mocked(panelPreferencesApi.savePanelPreferences).mockImplementation((preference) =>
      Promise.resolve(preference),
    )
  })

  it('hydrates the opacity preference from the server on mount', async () => {
    render(<ViewerTab />)

    await waitFor(() => expect(panelPreferencesApi.getPanelPreferences).toHaveBeenCalled())
  })

  it('renders the opacity slider bounded to [40, 100]', () => {
    render(<ViewerTab />)

    const slider = screen.getByRole('slider', { name: /panel opacity/i })
    expect(slider).toHaveAttribute('aria-valuemin', '40')
    expect(slider).toHaveAttribute('aria-valuemax', '100')
  })

  it('saves a changed opacity value', async () => {
    render(<ViewerTab />)

    await usePanelPreferencesStore.getState().update({ opacityPercent: 60 })

    await waitFor(() =>
      expect(panelPreferencesApi.savePanelPreferences).toHaveBeenCalledWith(
        expect.objectContaining({ opacityPercent: 60 }),
      ),
    )
  })

  it('surfaces a save failure instead of failing silently', async () => {
    vi.mocked(panelPreferencesApi.savePanelPreferences).mockRejectedValue(new Error('Save failed.'))
    render(<ViewerTab />)

    await usePanelPreferencesStore.getState().update({ opacityPercent: 50 })

    expect(await screen.findByText('Save failed.')).toBeInTheDocument()
  })
})
