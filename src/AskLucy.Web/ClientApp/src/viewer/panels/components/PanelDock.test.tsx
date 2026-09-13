import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { RESERVED_ATTRIBUTE } from '../layout/reservedRegions'
import { panelTypeRegistry } from '../registry'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import { PanelDock } from './PanelDock'

const TEST_TYPE_KEY = `dock-test-panel-${Math.random()}`
panelTypeRegistry.register({
  typeKey: TEST_TYPE_KEY,
  renderer: () => null,
  schema: z.object({ label: z.string() }),
  chrome: { titleBar: true, resizable: true, defaultSize: { width: 200, height: 150 } },
})

const initialState = useFloatingPanelStore.getState()

describe('PanelDock (specs/054 FR-005e, FR-007, FR-008 — US1 slice)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('renders nothing when no panels are open', () => {
    const { container } = render(<PanelDock onArrange={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders the arrange action once a panel is open, and declares itself reserved', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'dock-1', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    const { container } = render(<PanelDock onArrange={vi.fn()} />)

    expect(screen.getByRole('button', { name: /arrange panels/i })).toBeInTheDocument()
    expect(container.querySelector(`[${RESERVED_ATTRIBUTE}]`)).not.toBeNull()
  })

  it('calls onArrange when the arrange action is clicked', async () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'dock-2', typeKey: TEST_TYPE_KEY, title: 'A', data: { label: 'a' } })
    const onArrange = vi.fn()
    render(<PanelDock onArrange={onArrange} />)

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: /arrange panels/i }))

    expect(onArrange).toHaveBeenCalledTimes(1)
  })
})

describe('PanelDock reopen tray (specs/054 FR-006, FR-007, FR-008, FR-009 — US2 slice)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('renders nothing when there are no open panels and nothing has been closed', () => {
    const { container } = render(<PanelDock onArrange={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders the reopen list (and no arrange action) when everything has been closed', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'dock-closed-1', typeKey: TEST_TYPE_KEY, title: 'Closed Panel', data: { label: 'a' } })
    useFloatingPanelStore.getState().closePanel('dock-closed-1')

    render(<PanelDock onArrange={vi.fn()} />)

    expect(screen.getByText('Closed Panel')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /arrange panels/i })).not.toBeInTheDocument()
  })

  it('renders both the arrange action and the reopen list when some panels are open and some are closed', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'dock-open-1', typeKey: TEST_TYPE_KEY, title: 'Open', data: { label: 'a' } })
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'dock-closed-2', typeKey: TEST_TYPE_KEY, title: 'Closed Later', data: { label: 'a' } })
    useFloatingPanelStore.getState().closePanel('dock-closed-2')

    render(<PanelDock onArrange={vi.fn()} />)

    expect(screen.getByRole('button', { name: /arrange panels/i })).toBeInTheDocument()
    expect(screen.getByText('Closed Later')).toBeInTheDocument()
  })

  it('reopens the corresponding panel when a tray entry is clicked, and removes it from the list', async () => {
    useFloatingPanelStore.getState().openPanel({
      kind: 'live',
      requestId: 'dock-reopen',
      typeKey: TEST_TYPE_KEY,
      title: 'Reopen Target',
      data: { label: 'a' },
    })
    useFloatingPanelStore.getState().closePanel('dock-reopen')

    render(<PanelDock onArrange={vi.fn()} />)
    const user = userEvent.setup()
    await user.click(screen.getByText('Reopen Target'))

    expect(useFloatingPanelStore.getState().panels.some((p) => p.id === 'dock-reopen')).toBe(true)
    expect(useFloatingPanelStore.getState().closedPanels).toHaveLength(0)
  })
})
