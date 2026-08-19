import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as panelPreferencesApi from '../api/panelPreferencesApi'
import { usePanelPreferencesStore } from '../../../viewer/panels/store/panelPreferencesStore'
import { ViewerTab } from './ViewerTab'

expect.extend(toHaveNoViolations)

vi.mock('../api/panelPreferencesApi')

describe('ViewerTab accessibility (opacity slider)', () => {
  beforeEach(() => {
    usePanelPreferencesStore.setState({ opacityPercent: 85, error: null })
    vi.mocked(panelPreferencesApi.getPanelPreferences).mockResolvedValue({ opacityPercent: 85 })
  })

  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(<ViewerTab />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
