import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import type { FloatingPanel as FloatingPanelModel } from '../types/panel'
import { FloatingPanel } from './FloatingPanel'

expect.extend(toHaveNoViolations)

vi.mock('../store/floatingPanelStore', () => ({
  useFloatingPanelStore: (selector: (s: Record<string, () => void>) => unknown) =>
    selector({
      closePanel: vi.fn(),
      focusPanel: vi.fn(),
      minimizePanel: vi.fn(),
      restorePanel: vi.fn(),
      updatePosition: vi.fn(),
      updateSize: vi.fn(),
    }),
}))

vi.mock('react-rnd', () => ({
  Rnd: (props: { children: React.ReactNode; onMouseDown?: () => void }) => (
    <div onMouseDown={props.onMouseDown}>{props.children}</div>
  ),
}))

function makePanel(overrides: Partial<FloatingPanelModel> = {}): FloatingPanelModel {
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
    ...overrides,
  }
}

describe('FloatingPanel accessibility — normal (drag handle, minimize, close)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(<FloatingPanel panel={makePanel()} />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('exposes minimize and close as labeled, keyboard-focusable buttons', () => {
    render(<FloatingPanel panel={makePanel()} />)
    for (const name of [/minimize panel/i, /close panel/i]) {
      const button = screen.getByRole('button', { name })
      expect(button).toBeVisible()
      expect(button.tabIndex).not.toBe(-1)
    }
  })
})

describe('FloatingPanel accessibility — minimized (restore, close)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(<FloatingPanel panel={makePanel({ minimized: true })} />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('exposes restore and close as labeled, keyboard-focusable buttons', () => {
    render(<FloatingPanel panel={makePanel({ minimized: true })} />)
    for (const name of [/restore panel/i, /close panel/i]) {
      const button = screen.getByRole('button', { name })
      expect(button).toBeVisible()
      expect(button.tabIndex).not.toBe(-1)
    }
  })
})
