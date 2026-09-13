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
    kind: 'live',
    typeKey: 'unregistered-type',
    title: 'Test Panel',
    data: {},
    validationStatus: 'unknown-type',
    validationError: null,
    position: { x: 40, y: 40 },
    size: { width: 400, height: 300 },
    chrome: { titleBar: true, resizable: true, defaultSize: { width: 400, height: 300 } },
    minimized: false,
    restoreState: null,
    zOrder: 1,
    lastFocusedAtUtc: Date.now(),
    opacityOverride: null,
    contextAssociation: null,
    contextStatus: null,
    manuallyPlaced: false,
    ...overrides,
  }
}

describe('FloatingPanel fallback rendering', () => {
  it('renders a visible fallback for an unknown panel type, never blank', () => {
    render(<FloatingPanel panel={makePanel({ validationStatus: 'unknown-type', typeKey: 'mystery' })} />)
    expect(screen.getByText(/unsupported panel type/i)).toBeInTheDocument()
    expect(screen.getByText(/mystery/)).toBeInTheDocument()
  })

  it('never reaches the unknown-type fallback for a content panel (specs/049 FR-025, T059) — only a live panel can', () => {
    render(
      <FloatingPanel
        panel={makePanel({ kind: 'content', validationStatus: 'unknown-type', typeKey: undefined, content: undefined })}
      />,
    )
    expect(screen.queryByText(/unsupported panel type/i)).not.toBeInTheDocument()
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
    render(<FloatingPanel panel={makePanel({ chrome: { titleBar: true, resizable: true, defaultSize: { width: 400, height: 300 } } })} />)
    expect(lastRndProps.enableResizing).toBe(true)

    render(
      <FloatingPanel
        panel={makePanel({ id: 'fixed', chrome: { titleBar: true, resizable: false, defaultSize: { width: 400, height: 300 } } })}
      />,
    )
    expect(lastRndProps.enableResizing).toBe(false)
  })

  it('updates floatingPanelStore position when Rnd reports a drag stop', () => {
    render(<FloatingPanel panel={makePanel({ id: 'panel-drag' })} />)
    const onDragStop = lastRndProps.onDragStop as (e: unknown, data: { x: number; y: number }) => void
    onDragStop(undefined, { x: 123, y: 456 })
    expect(updatePositionMock).toHaveBeenCalledWith('panel-drag', { x: 123, y: 456 })
  })

  describe('specs/054 drag-time placement wiring (FR-005f/FR-005g, D6)', () => {
    it('calls onDragStart when Rnd reports a drag start', () => {
      const onDragStart = vi.fn()
      render(<FloatingPanel panel={makePanel({ id: 'panel-drag-start' })} onDragStart={onDragStart} />)
      const rndOnDragStart = lastRndProps.onDragStart as () => void
      rndOnDragStart()
      expect(onDragStart).toHaveBeenCalledTimes(1)
    })

    it('calls onDragMove with the panel’s CENTER (not its raw top-left) on every drag move, alongside the existing live-follow updatePosition', () => {
      const onDragMove = vi.fn()
      // Default makePanel size is 400x300 (half-width 200, half-height 150).
      render(<FloatingPanel panel={makePanel({ id: 'panel-drag-move' })} onDragMove={onDragMove} />)
      const onDrag = lastRndProps.onDrag as (e: unknown, data: { x: number; y: number }) => void
      onDrag(undefined, { x: 77, y: 88 })
      expect(updatePositionMock).toHaveBeenCalledWith('panel-drag-move', { x: 77, y: 88 })
      expect(onDragMove).toHaveBeenCalledWith({ x: 277, y: 238 })
    })

    it('applies the position onDragEnd returns (a snap) instead of the raw drop point, called with the CENTER', () => {
      const onDragEnd = vi.fn().mockReturnValue({ x: 999, y: 111 })
      // Default makePanel size is 400x300 (half-width 200, half-height 150).
      render(<FloatingPanel panel={makePanel({ id: 'panel-snap' })} onDragEnd={onDragEnd} />)
      const onDragStop = lastRndProps.onDragStop as (e: unknown, data: { x: number; y: number }) => void
      onDragStop(undefined, { x: 200, y: 200 })
      expect(onDragEnd).toHaveBeenCalledWith({ x: 400, y: 350 })
      expect(updatePositionMock).toHaveBeenCalledWith('panel-snap', { x: 999, y: 111 })
    })

    it('falls back to the raw drop point when no onDragEnd is wired', () => {
      render(<FloatingPanel panel={makePanel({ id: 'panel-no-snap' })} />)
      const onDragStop = lastRndProps.onDragStop as (e: unknown, data: { x: number; y: number }) => void
      onDragStop(undefined, { x: 321, y: 654 })
      expect(updatePositionMock).toHaveBeenCalledWith('panel-no-snap', { x: 321, y: 654 })
    })
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

  it('renders a compact bar (no minimize control) when minimized, with a working restore button', async () => {
    const user = userEvent.setup()
    render(<FloatingPanel panel={makePanel({ id: 'panel-restore', minimized: true })} />)
    expect(screen.queryByRole('button', { name: /minimize panel/i })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /restore panel/i }))
    expect(restorePanelMock).toHaveBeenCalledWith('panel-restore')
  })

  // specs/054 (feedback 2026-09-13) — a minimized panel used to be a plain positioned Box, not
  // wrapped in Rnd at all, so it couldn't be dragged. Several panels can now minimize into
  // overlapping bars with no collision avoidance of their own, so it's wired through the same
  // Rnd + onDragStart/onDragMove/onDragEnd chain the full panel uses.
  it('is draggable while minimized, wired through the same onDragStart/onDragMove/onDragEnd chain as a full panel', () => {
    const onDragStart = vi.fn()
    const onDragMove = vi.fn()
    const onDragEnd = vi.fn().mockReturnValue({ x: 77, y: 88 })
    render(
      <FloatingPanel
        panel={makePanel({ id: 'panel-min-drag', minimized: true, position: { x: 10, y: 20 } })}
        onDragStart={onDragStart}
        onDragMove={onDragMove}
        onDragEnd={onDragEnd}
      />,
    )

    expect(lastRndProps.position).toEqual({ x: 10, y: 20 })
    expect(lastRndProps.enableResizing).toBe(false)

    const rndOnDragStart = lastRndProps.onDragStart as () => void
    rndOnDragStart()
    expect(onDragStart).toHaveBeenCalledTimes(1)

    // MINIMIZED_BAR_WIDTH/HEIGHT (220x40) — half-width 110, half-height 20.
    const onDrag = lastRndProps.onDrag as (e: unknown, data: { x: number; y: number }) => void
    onDrag(undefined, { x: 30, y: 40 })
    expect(updatePositionMock).toHaveBeenCalledWith('panel-min-drag', { x: 30, y: 40 })
    expect(onDragMove).toHaveBeenCalledWith({ x: 140, y: 60 })

    const onDragStop = lastRndProps.onDragStop as (e: unknown, data: { x: number; y: number }) => void
    onDragStop(undefined, { x: 50, y: 60 })
    expect(onDragEnd).toHaveBeenCalledWith({ x: 160, y: 80 })
    expect(updatePositionMock).toHaveBeenCalledWith('panel-min-drag', { x: 77, y: 88 })
  })
})

describe('FloatingPanel chrome variants (specs/049 User Story 3)', () => {
  it('shows a title bar with the title, minimize and close controls when the panel declares one', () => {
    render(
      <FloatingPanel
        panel={makePanel({ chrome: { titleBar: true, resizable: true, defaultSize: { width: 400, height: 300 } } })}
      />,
    )
    expect(screen.getByText('Test Panel')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /minimize panel/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /close panel/i })).toBeInTheDocument()
  })

  it('shows no title text when the panel declares no title bar, but keeps minimize/close reachable', () => {
    render(
      <FloatingPanel
        panel={makePanel({ chrome: { titleBar: false, resizable: false, defaultSize: { width: 96, height: 96 } } })}
      />,
    )
    expect(screen.queryByText('Test Panel')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /minimize panel/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /close panel/i })).toBeInTheDocument()
  })

  it('exposes the no-title-bar grip as a focusable, labelled group supporting arrow-key movement', async () => {
    const user = userEvent.setup()
    render(
      <FloatingPanel
        panel={makePanel({
          id: 'panel-grip',
          position: { x: 50, y: 50 },
          chrome: { titleBar: false, resizable: false, defaultSize: { width: 96, height: 96 } },
        })}
      />,
    )
    const grip = screen.getByRole('group', { name: /use arrow keys to move/i })
    grip.focus()
    await user.keyboard('{ArrowRight}')
    expect(updatePositionMock).toHaveBeenCalledWith('panel-grip', { x: 60, y: 50 })
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
