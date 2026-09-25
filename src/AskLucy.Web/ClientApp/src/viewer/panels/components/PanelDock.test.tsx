import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
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

describe('PanelDock reopen tray (specs/054 FR-006, FR-007, FR-008, FR-009 — US2 slice)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('renders nothing when there are no open panels and nothing has been closed', () => {
    const { container } = render(<PanelDock />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing while panels are open but none has been closed', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'dock-open-only', typeKey: TEST_TYPE_KEY, title: 'Open', data: { label: 'a' } })

    // "Arrange panels" moved to the viewer toolbar (panelsExtension), so an open panel alone
    // leaves the dock with nothing to show.
    const { container } = render(<PanelDock />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders the reopen list, declares itself reserved, and no arrange action', () => {
    useFloatingPanelStore
      .getState()
      .openPanel({ kind: 'live', requestId: 'dock-closed-1', typeKey: TEST_TYPE_KEY, title: 'Closed Panel', data: { label: 'a' } })
    useFloatingPanelStore.getState().closePanel('dock-closed-1')

    const { container } = render(<PanelDock />)

    expect(screen.getByText('Closed Panel')).toBeInTheDocument()
    expect(container.querySelector(`[${RESERVED_ATTRIBUTE}]`)).not.toBeNull()
    expect(screen.queryByRole('button', { name: /arrange panels/i })).not.toBeInTheDocument()
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

    render(<PanelDock />)
    const user = userEvent.setup()
    await user.click(screen.getByText('Reopen Target'))

    expect(useFloatingPanelStore.getState().panels.some((p) => p.id === 'dock-reopen')).toBe(true)
    expect(useFloatingPanelStore.getState().closedPanels).toHaveLength(0)
  })
})
