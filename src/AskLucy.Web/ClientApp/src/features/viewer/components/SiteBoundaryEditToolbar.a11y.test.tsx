import { ThemeProvider } from '@mui/material'
import { act, cleanup, render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { afterEach, describe, expect, it } from 'vitest'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import { createAppTheme } from '../../../theme'
import { useSiteBoundaryEditStore, type ViewState } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { SiteBoundaryEditToolbar } from './SiteBoundaryEditToolbar'

expect.extend(toHaveNoViolations)

const P = (east: number, north: number): GeoPoint => ({ latitude: 23.59 + north / 111_320, longitude: 58.4 + east / 102_000 })
const viewState: ViewState = { mode: 'isometric', rotationEnabled: true, center: { latitude: 23.59, longitude: 58.4 }, zoom: 17, heading: 0, tilt: 45 }
const store = () => useSiteBoundaryEditStore.getState()

const enter = () =>
  store().enter({ chatId: 'chat-1', siteName: 'Muscat Grand Mall', revision: 'rev-1', rings: [[P(0, 0), P(100, 0), P(100, 100), P(0, 100)]], viewState })
const edit = () => store().applyChange({ op: 'move', ring: 0, index: 2, before: P(100, 100), after: P(120, 120) })

afterEach(() => {
  cleanup()
  store().end()
})

// specs/079 FR-014 / SC-008: WCAG 2.1 AA for the edit controls, in every state the bar can be in.
describe('SiteBoundaryEditToolbar accessibility', () => {
  describe.each(['light', 'dark'] as const)('%s theme', (mode) => {
    const states: [string, () => void][] = [
      ['untouched', () => {}],
      ['after a change', () => edit()],
      ['with a refusal', () => store().refuse('That would make the outline cross itself.')],
      ['saving', () => { edit(); store().beginSave() }],
      ['after a failed save', () => { edit(); store().beginSave(); store().saveFailed('The outline could not be saved.') }],
      ['after a conflict', () => { edit(); store().beginSave(); store().conflict('rev-9') }],
      ['with the bar dismissed and a message showing', () => { store().setToolbarHidden(true); store().refuse('That would make the outline cross itself.') }],
    ]

    it.each(states)('has no automatically detectable a11y violations (%s)', async (_name, arrange) => {
      enter()
      act(() => arrange())

      const { container } = render(
        <ThemeProvider theme={createAppTheme(mode)}>
          <SiteBoundaryEditToolbar />
        </ThemeProvider>,
      )

      expect(await axe(container)).toHaveNoViolations()
    })
  })
})
