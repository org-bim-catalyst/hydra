import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useWorkspaceOverlayStore } from '../../store/workspaceOverlayStore'
import { FloatingPanel } from './FloatingPanel'

function resetStore() {
  useWorkspaceOverlayStore.setState({ expandedControlId: null, viewMode: '3D', unreadControlIds: new Set() })
}

describe('FloatingPanel', () => {
  beforeEach(() => {
    resetStore()
  })

  it('moves focus to the first focusable child when it becomes open', () => {
    useWorkspaceOverlayStore.setState({ expandedControlId: 'chat' })
    render(
      <FloatingPanel controlId="chat" titleId="Ask Lucy assistant" onRequestClose={() => {}}>
        <button type="button">First focusable</button>
      </FloatingPanel>,
    )
    expect(screen.getByRole('button', { name: 'First focusable' })).toHaveFocus()
  })

  it('does not steal focus when a different control is expanded', () => {
    useWorkspaceOverlayStore.setState({ expandedControlId: 'layers' })
    render(
      <FloatingPanel controlId="chat" titleId="Ask Lucy assistant" onRequestClose={() => {}}>
        <button type="button">First focusable</button>
      </FloatingPanel>,
    )
    expect(screen.getByRole('button', { name: 'First focusable' })).not.toHaveFocus()
  })

  it('calls onRequestClose when the in-panel close button is clicked', async () => {
    const onRequestClose = vi.fn()
    useWorkspaceOverlayStore.setState({ expandedControlId: 'chat' })
    render(
      <FloatingPanel controlId="chat" titleId="Ask Lucy assistant" onRequestClose={onRequestClose}>
        <button type="button">Content</button>
      </FloatingPanel>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onRequestClose).toHaveBeenCalledTimes(1)
  })

  it('exposes an accessible region labeled with titleId', () => {
    render(
      <FloatingPanel controlId="chat" titleId="Ask Lucy assistant" onRequestClose={() => {}}>
        <button type="button">Content</button>
      </FloatingPanel>,
    )
    expect(screen.getByRole('region', { name: 'Ask Lucy assistant' })).toBeInTheDocument()
  })
})
