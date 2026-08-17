import { render } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { useWorkspaceOverlayStore } from '../../../store/workspaceOverlayStore'
import { WorkspaceSurface } from './WorkspaceSurface'

function resetStore() {
  useWorkspaceOverlayStore.setState({ expandedControlId: null, viewMode: '3D', unreadControlIds: new Set() })
}

describe('WorkspaceSurface', () => {
  beforeEach(() => {
    resetStore()
  })

  it('renders a full-bleed, non-interactive background with no canvas element', () => {
    const { container } = render(<WorkspaceSurface />)
    const root = container.firstElementChild as HTMLElement
    expect(root).toHaveAttribute('aria-hidden', 'true')
    expect(container.querySelector('canvas')).not.toBeInTheDocument()
  })

  it('reflects the active view mode via a data attribute (FR-011)', () => {
    useWorkspaceOverlayStore.setState({ viewMode: '2D' })
    const { container } = render(<WorkspaceSurface />)
    const root = container.firstElementChild as HTMLElement
    expect(root).toHaveAttribute('data-view-mode', '2D')
  })
})
