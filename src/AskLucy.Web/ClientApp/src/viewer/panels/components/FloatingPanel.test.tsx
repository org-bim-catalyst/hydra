import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { FloatingPanel as FloatingPanelModel } from '../types/panel'
import { FloatingPanel } from './FloatingPanel'

const closePanelMock = vi.fn()
const focusPanelMock = vi.fn()
const minimizePanelMock = vi.fn()
const restorePanelMock = vi.fn()
const updatePositionMock = vi.fn()
const updateSizeMock = vi.fn()

vi.mock('../store/floatingPanelStore', () => ({
  useFloatingPanelStore: (
    selector: (s: {
      closePanel: typeof closePanelMock
      focusPanel: typeof focusPanelMock
      minimizePanel: typeof minimizePanelMock
      restorePanel: typeof restorePanelMock
      updatePosition: typeof updatePositionMock
      updateSize: typeof updateSizeMock
    }) => unknown,
  ) =>
    selector({
      closePanel: closePanelMock,
      focusPanel: focusPanelMock,
      minimizePanel: minimizePanelMock,
      restorePanel: restorePanelMock,
      updatePosition: updatePositionMock,
      updateSize: updateSizeMock,
    }),
}))

const selectMock = vi.fn()

vi.mock('../../engine/viewerEngineInstance', () => ({
  viewerEngine: { select: (layerId: string, elementId: string) => selectMock(layerId, elementId) },
}))

let lastRndProps: Record<string, unknown> = {}

vi.mock('react-rnd', () => ({
  Rnd: (props: Record<string, unknown> & { children: React.ReactNode; onMouseDown?: () => void }) => {
    lastRndProps = props
    return (
      <div data-testid="rnd-mock" onMouseDown={props.onMouseDown}>
        {props.children}
      </div>
    )
  },
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

describe('FloatingPanel drag/resize wiring (US2, FR-004/FR-005/FR-018)', () => {
  it('passes controlled position/size and parent-relative bounds to Rnd', () => {
    render(<FloatingPanel panel={makePanel({ position: { x: 10, y: 20 }, size: { width: 500, height: 350 } })} />)
    expect(lastRndProps.position).toEqual({ x: 10, y: 20 })
    expect(lastRndProps.size).toEqual({ width: 500, height: 350 })
    expect(lastRndProps.bounds).toBe('parent')
  })

  it('enables resizing for a resizable panel and disables it for a fixed-size one', () => {
    render(<FloatingPanel panel={makePanel({ resizable: true })} />)
    expect(lastRndProps.enableResizing).toBe(true)

    render(<FloatingPanel panel={makePanel({ id: 'fixed', resizable: false })} />)
    expect(lastRndProps.enableResizing).toBe(false)
  })

  it('updates floatingPanelStore position when Rnd reports a drag stop', () => {
    render(<FloatingPanel panel={makePanel({ id: 'panel-drag' })} />)
    const onDragStop = lastRndProps.onDragStop as (e: unknown, data: { x: number; y: number }) => void
    onDragStop(undefined, { x: 123, y: 456 })
    expect(updatePositionMock).toHaveBeenCalledWith('panel-drag', { x: 123, y: 456 })
  })

  it('updates floatingPanelStore size and position when Rnd reports a resize stop', () => {
    render(<FloatingPanel panel={makePanel({ id: 'panel-resize' })} />)
    const onResizeStop = lastRndProps.onResizeStop as (
      e: unknown,
      dir: unknown,
      ref: { offsetWidth: number; offsetHeight: number },
      delta: unknown,
      position: { x: number; y: number },
    ) => void
    onResizeStop(undefined, undefined, { offsetWidth: 600, offsetHeight: 400 }, undefined, { x: 5, y: 5 })
    expect(updateSizeMock).toHaveBeenCalledWith('panel-resize', { width: 600, height: 400 })
    expect(updatePositionMock).toHaveBeenCalledWith('panel-resize', { x: 5, y: 5 })
  })

  it('focuses the panel on mousedown', async () => {
    const user = userEvent.setup()
    render(<FloatingPanel panel={makePanel({ id: 'panel-focus' })} />)
    await user.pointer({ keys: '[MouseLeft>]', target: screen.getByTestId('rnd-mock') })
    expect(focusPanelMock).toHaveBeenCalledWith('panel-focus')
  })
})

describe('FloatingPanel keyboard-only repositioning (T088 — react-rnd drag has no built-in keyboard equivalent)', () => {
  it('nudges the panel position with arrow keys when the title bar is focused', async () => {
    const user = userEvent.setup()
    render(<FloatingPanel panel={makePanel({ id: 'panel-kbd', position: { x: 50, y: 50 } })} />)
    const handle = screen.getByRole('group', { name: /use arrow keys to move/i })
    handle.focus()

    await user.keyboard('{ArrowRight}')
    expect(updatePositionMock).toHaveBeenCalledWith('panel-kbd', { x: 60, y: 50 })

    await user.keyboard('{Shift>}{ArrowDown}{/Shift}')
    expect(updatePositionMock).toHaveBeenCalledWith('panel-kbd', { x: 50, y: 90 })
  })

  it('never nudges to a negative position', async () => {
    const user = userEvent.setup()
    render(<FloatingPanel panel={makePanel({ id: 'panel-kbd-edge', position: { x: 5, y: 5 } })} />)
    const handle = screen.getByRole('group', { name: /use arrow keys to move/i })
    handle.focus()

    await user.keyboard('{ArrowLeft}')

    expect(updatePositionMock).toHaveBeenCalledWith('panel-kbd-edge', { x: 0, y: 5 })
  })
})

describe('FloatingPanel minimize/restore (US2, FR-006)', () => {
  it('calls minimizePanel when the minimize button is clicked', async () => {
    const user = userEvent.setup()
    render(<FloatingPanel panel={makePanel({ id: 'panel-min' })} />)
    await user.click(screen.getByRole('button', { name: /minimize panel/i }))
    expect(minimizePanelMock).toHaveBeenCalledWith('panel-min')
  })

  it('renders a compact bar (no Rnd chrome) when minimized, with a working restore button', async () => {
    const user = userEvent.setup()
    render(<FloatingPanel panel={makePanel({ id: 'panel-restore', minimized: true })} />)
    expect(screen.queryByTestId('rnd-mock')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /minimize panel/i })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /restore panel/i }))
    expect(restorePanelMock).toHaveBeenCalledWith('panel-restore')
  })
})

describe('FloatingPanel context association (US4, FR-013/FR-014)', () => {
  it('shows no Locate button and no stale/invalid indicator when there is no context association', () => {
    render(<FloatingPanel panel={makePanel({ contextAssociation: null, contextStatus: null })} />)
    expect(screen.queryByRole('button', { name: /locate in viewer/i })).not.toBeInTheDocument()
  })

  it('calls viewerEngine.select with the associated layer/element when Locate is clicked', async () => {
    const user = userEvent.setup()
    render(
      <FloatingPanel
        panel={makePanel({
          contextAssociation: { layerId: 'layer-1', elementId: 'element-1' },
          contextStatus: 'current',
        })}
      />,
    )
    await user.click(screen.getByRole('button', { name: /locate in viewer/i }))
    expect(selectMock).toHaveBeenCalledWith('layer-1', 'element-1')
  })

  it('shows a visible stale indicator when contextStatus is stale', () => {
    render(
      <FloatingPanel
        panel={makePanel({ contextAssociation: { layerId: 'layer-1', elementId: null }, contextStatus: 'stale' })}
      />,
    )
    expect(screen.getByRole('img', { name: /association is stale/i })).toBeInTheDocument()
  })

  it('shows a visible invalid indicator when contextStatus is invalid', () => {
    render(
      <FloatingPanel
        panel={makePanel({ contextAssociation: { layerId: 'layer-1', elementId: null }, contextStatus: 'invalid' })}
      />,
    )
    expect(screen.getByRole('img', { name: /association is no longer valid/i })).toBeInTheDocument()
  })
})
