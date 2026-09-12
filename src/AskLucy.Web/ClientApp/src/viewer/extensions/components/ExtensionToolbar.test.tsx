import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'
import { ExtensionToolbar } from './ExtensionToolbar'

const initialState = useViewerExtensionStore.getState()

function TestIcon() {
  return <svg data-testid="test-icon" />
}

describe('ExtensionToolbar (FR-021, FR-022, FR-023, research D6)', () => {
  beforeEach(() => {
    useViewerExtensionStore.setState(initialState, true)
  })

  it('renders nothing (not an empty frame) when no entry has been contributed (FR-023)', () => {
    const { container } = render(<ExtensionToolbar />)
    expect(container.textContent).toBe('')
    expect(container.querySelector('*')).toBeNull()
  })

  it('renders a contributed entry, operable via its onClick', async () => {
    const onClick = vi.fn()
    useViewerExtensionStore.getState().addContribution({
      kind: 'toolbarEntry',
      extensionId: 'ext-a',
      entry: { id: 'a-1', label: 'Solar Analysis', icon: TestIcon, onClick },
    })

    render(<ExtensionToolbar />)
    const user = userEvent.setup()
    const button = screen.getByRole('button', { name: 'Solar Analysis' })
    await user.click(button)

    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('disappears when its extension stops (contributions withdrawn)', () => {
    useViewerExtensionStore.getState().addContribution({
      kind: 'toolbarEntry',
      extensionId: 'ext-b',
      entry: { id: 'b-1', label: 'Entry B', icon: TestIcon, onClick: () => {} },
    })

    const { rerender } = render(<ExtensionToolbar />)
    expect(screen.getByRole('button', { name: 'Entry B' })).toBeInTheDocument()

    act(() => {
      useViewerExtensionStore.getState().removeContributionsFor('ext-b')
    })
    rerender(<ExtensionToolbar />)

    expect(screen.queryByRole('button', { name: 'Entry B' })).not.toBeInTheDocument()
  })

  it('renders entries from two extensions in stable, contribution order (FR-022)', () => {
    useViewerExtensionStore.getState().addContribution({
      kind: 'toolbarEntry',
      extensionId: 'ext-first',
      entry: { id: 'first-1', label: 'First', icon: TestIcon, onClick: () => {} },
    })
    useViewerExtensionStore.getState().addContribution({
      kind: 'toolbarEntry',
      extensionId: 'ext-second',
      entry: { id: 'second-1', label: 'Second', icon: TestIcon, onClick: () => {} },
    })

    render(<ExtensionToolbar />)
    const buttons = screen.getAllByRole('button')
    expect(buttons.map((b) => b.getAttribute('aria-label'))).toEqual(['First', 'Second'])
  })
})
