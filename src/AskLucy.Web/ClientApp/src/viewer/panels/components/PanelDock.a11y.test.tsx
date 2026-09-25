import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { beforeEach, describe, expect, it } from 'vitest'
import { z } from 'zod'
import { panelTypeRegistry } from '../registry'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import { PanelDock } from './PanelDock'

expect.extend(toHaveNoViolations)

const TEST_TYPE_KEY = `dock-a11y-panel-${Math.random()}`
panelTypeRegistry.register({
  typeKey: TEST_TYPE_KEY,
  renderer: () => null,
  schema: z.object({ label: z.string() }),
  chrome: { titleBar: true, resizable: true, defaultSize: { width: 200, height: 150 } },
})

const initialState = useFloatingPanelStore.getState()

describe('PanelDock accessibility', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('has no automatically detectable a11y violations when rendering the reopen list', async () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'dock-a11y-3',
      typeKey: TEST_TYPE_KEY,
      title: 'Closed Entry',
      data: { label: 'a' },
    })
    useFloatingPanelStore.getState().closePanel('dock-a11y-3')
    const { container } = render(<PanelDock />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('exposes each reopen entry as a keyboard-focusable control', () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'dock-a11y-4',
      typeKey: TEST_TYPE_KEY,
      title: 'Closed Entry',
      data: { label: 'a' },
    })
    useFloatingPanelStore.getState().closePanel('dock-a11y-4')
    const { getByText } = render(<PanelDock />)
    const entry = getByText('Closed Entry')
    expect(entry.tagName.toLowerCase()).toBe('button')
    expect(entry.tabIndex).not.toBe(-1)
  })
})
