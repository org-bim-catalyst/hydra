import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { useWorkspaceOverlayStore } from '../../store/workspaceOverlayStore'
import { WorkspaceOverlay } from './WorkspaceOverlay'
import type { ControlDefinition } from './types'

function resetStore() {
  useWorkspaceOverlayStore.setState({
    expandedControlId: null,
    viewMode: '3D',
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
})
