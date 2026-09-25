import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { RESERVED_ATTRIBUTE } from '../../viewer/panels/layout/reservedRegions'
import { useWorkspaceOverlayStore } from '../../store/workspaceOverlayStore'
import { WorkspaceOverlay } from './WorkspaceOverlay'
import type { ControlDefinition } from './types'

function resetStore() {
  useWorkspaceOverlayStore.setState({
    expandedControlId: null,
    viewMode: 'isometric',
    unreadControlIds: new Set(),
  })
}

const controls: ControlDefinition[] = [
  {
    id: 'layers',
    label: 'Layers',
    icon: <span aria-hidden="true">L</span>,
    status: 'functional',
    kind: 'action-group',
    placement: 'right-stack',
    content: <div>Layers content</div>,
  },
  {
    id: 'chat',
    label: 'Chat',
    icon: <span aria-hidden="true">C</span>,
    status: 'functional',
    kind: 'panel',
    placement: 'bottom-end',
    content: <div>Chat content</div>,
  },
]

describe('WorkspaceOverlay', () => {
  beforeEach(() => {
    resetStore()
  })

  it('renders one CircularAction trigger per ControlDefinition', () => {
    render(<WorkspaceOverlay controls={controls} />)
    expect(screen.getByRole('button', { name: 'Layers' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Chat' })).toBeInTheDocument()
  })

  it('only the control matching expandedControlId is expanded', () => {
    useWorkspaceOverlayStore.setState({ expandedControlId: 'chat' })
    render(<WorkspaceOverlay controls={controls} />)
    expect(screen.getByRole('button', { name: 'Layers' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByRole('button', { name: 'Chat' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('renders children independent of expandedControlId', () => {
    render(
      <WorkspaceOverlay controls={controls}>
        <div data-testid="ai-presence">presence</div>
      </WorkspaceOverlay>,
    )
    expect(screen.getByTestId('ai-presence')).toBeInTheDocument()
  })

  it('renders no controls, without error, when controls is empty', () => {
    render(<WorkspaceOverlay controls={[]} />)
    expect(screen.queryAllByRole('button')).toHaveLength(0)
  })

  it('groups controls into separate clusters by placement', () => {
    const grouped: ControlDefinition[] = [
      ...controls,
      {
        id: 'account',
        label: 'Account',
        icon: <span aria-hidden="true">A</span>,
        status: 'functional',
        kind: 'action-group',
        placement: 'top-cluster',
        content: <div>Account content</div>,
      },
    ]
    render(<WorkspaceOverlay controls={grouped} />)
    expect(screen.getByRole('button', { name: 'Layers' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Chat' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Account' })).toBeInTheDocument()
  })

  // specs/073 contract W1–W6 — the studio's top-left HUD row.
  describe('topStart', () => {
    const withAccount: ControlDefinition[] = [
      ...controls,
      {
        id: 'account',
        label: 'Account',
        icon: <span aria-hidden="true">A</span>,
        status: 'functional',
        kind: 'action-group',
        placement: 'top-cluster',
        content: <div>Account content</div>,
      },
    ]

    function renderWithTopStart() {
      return render(
        <WorkspaceOverlay controls={withAccount} topStart={<span data-testid="start-item">start</span>}>
          <div data-testid="child">child</div>
        </WorkspaceOverlay>,
      )
    }

    it('W1/W2: renders topStart in a reserved start group ahead of the top cluster, in one top bar', () => {
      renderWithTopStart()

      const startGroup = screen.getByTestId('start-item').parentElement as HTMLElement
      const cluster = screen.getByRole('button', { name: 'Account' }).closest(`[${RESERVED_ATTRIBUTE}]`) as HTMLElement

      expect(startGroup).toHaveAttribute(RESERVED_ATTRIBUTE)
      expect(startGroup.parentElement).toBe(cluster.parentElement)
      expect(startGroup.nextElementSibling).toBe(cluster)
      expect(window.getComputedStyle(startGroup).flexWrap).toBe('wrap')

      const topBar = startGroup.parentElement as HTMLElement
      expect(window.getComputedStyle(topBar).position).toBe('absolute')
      expect(window.getComputedStyle(topBar).display).toBe('flex')
      expect(window.getComputedStyle(topBar).pointerEvents).toBe('none')
    })

    it('W3: renders the top cluster in flow, still reserved, pushed to the right', () => {
      renderWithTopStart()

      const cluster = screen.getByRole('button', { name: 'Account' }).closest(`[${RESERVED_ATTRIBUTE}]`) as HTMLElement
      const style = window.getComputedStyle(cluster)

      expect(style.position).not.toBe('absolute')
      expect(style.marginLeft).toBe('auto')
    })

    it('W5: without topStart, the top cluster stays absolutely anchored as before', () => {
      render(<WorkspaceOverlay controls={withAccount} />)

      const cluster = screen.getByRole('button', { name: 'Account' }).closest(`[${RESERVED_ATTRIBUTE}]`) as HTMLElement
      expect(window.getComputedStyle(cluster).position).toBe('absolute')
    })

    it('W6: leaves the right-stack and bottom-end clusters and children untouched', () => {
      renderWithTopStart()

      for (const name of ['Layers', 'Chat']) {
        const cluster = screen.getByRole('button', { name }).closest(`[${RESERVED_ATTRIBUTE}]`) as HTMLElement
        expect(window.getComputedStyle(cluster).position).toBe('absolute')
        expect(cluster.contains(screen.getByTestId('start-item'))).toBe(false)
      }
      expect(screen.getByTestId('child')).toBeInTheDocument()
    })
  })
})
