import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { beforeEach, describe, expect, it, vi } from 'vitest'
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

  it('has no automatically detectable a11y violations when rendering the arrange action', async () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'dock-a11y-1', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    const { container } = render(<PanelDock onArrange={vi.fn()} />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('exposes the arrange action as a labeled, keyboard-focusable button', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'dock-a11y-2', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    const { getByRole } = render(<PanelDock onArrange={vi.fn()} />)
    const button = getByRole('button', { name: /arrange panels/i })
    expect(button).toBeVisible()
    expect(button.tabIndex).not.toBe(-1)
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
    const { container } = render(<PanelDock onArrange={vi.fn()} />)
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
    const { getByText } = render(<PanelDock onArrange={vi.fn()} />)
    const entry = getByText('Closed Entry')
    expect(entry.tagName.toLowerCase()).toBe('button')
    expect(entry.tabIndex).not.toBe(-1)
  })
})
