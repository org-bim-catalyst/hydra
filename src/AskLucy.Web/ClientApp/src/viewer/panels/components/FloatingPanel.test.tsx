import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { FloatingPanel as FloatingPanelModel } from '../types/panel'
import { FloatingPanel } from './FloatingPanel'

vi.mock('../store/floatingPanelStore', () => ({
  useFloatingPanelStore: (selector: (s: { closePanel: (id: string) => void }) => unknown) =>
    selector({ closePanel: closePanelMock }),
}))

const closePanelMock = vi.fn()

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

describe('FloatingPanel fallback rendering', () => {
  it('renders a visible fallback for an unknown panel type, never blank', () => {
    render(<FloatingPanel panel={makePanel({ validationStatus: 'unknown-type', typeKey: 'mystery' })} />)
    expect(screen.getByText(/unsupported panel type/i)).toBeInTheDocument()
    expect(screen.getByText(/mystery/)).toBeInTheDocument()
  })

  it('renders a distinct visible fallback for invalid data, including the validation error', () => {
    render(
      <FloatingPanel
        panel={makePanel({ validationStatus: 'invalid', validationError: 'label: Required' })}
      />,
    )
    expect(screen.getByText(/couldn't be loaded/i)).toBeInTheDocument()
    expect(screen.getByText('label: Required')).toBeInTheDocument()
  })

  it('calls closePanel with the panel id when the close button is clicked', async () => {
    const user = userEvent.setup()
    render(<FloatingPanel panel={makePanel({ id: 'panel-42' })} />)
    await user.click(screen.getByRole('button', { name: /close panel/i }))
    expect(closePanelMock).toHaveBeenCalledWith('panel-42')
  })
})
