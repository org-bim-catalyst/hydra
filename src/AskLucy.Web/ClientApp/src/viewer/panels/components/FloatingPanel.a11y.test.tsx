import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import type { FloatingPanel as FloatingPanelModel } from '../types/panel'
import { FloatingPanel } from './FloatingPanel'

expect.extend(toHaveNoViolations)

vi.mock('../store/floatingPanelStore', () => ({
  useFloatingPanelStore: (selector: (s: { closePanel: (id: string) => void }) => unknown) =>
    selector({ closePanel: vi.fn() }),
}))

function makePanel(): FloatingPanelModel {
  return {
    id: 'p1',
    typeKey: 'unregistered-type',
    title: 'Test Panel',
    data: {},
    validationStatus: 'unknown-type',
    validationError: null,
    position: { x: 40, y: 40 },
    size: { width: 400, height: 300 },
    resizable: true,
    minimized: false,
    restoreState: null,
    zOrder: 1,
    lastFocusedAtUtc: Date.now(),
    opacityOverride: null,
    contextAssociation: null,
    contextStatus: null,
  }
}

describe('FloatingPanel accessibility (close button)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(<FloatingPanel panel={makePanel()} />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('exposes the close control as a labeled, keyboard-focusable button', () => {
    render(<FloatingPanel panel={makePanel()} />)
    const closeButton = screen.getByRole('button', { name: /close panel/i })
    expect(closeButton).toBeVisible()
    expect(closeButton.tabIndex).not.toBe(-1)
  })
})
