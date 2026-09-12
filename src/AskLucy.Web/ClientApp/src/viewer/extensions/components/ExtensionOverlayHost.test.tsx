import { act, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'
import { ExtensionOverlayHost } from './ExtensionOverlayHost'

const initialState = useViewerExtensionStore.getState()

describe('ExtensionOverlayHost (FR-018, FR-020, research D3)', () => {
  beforeEach(() => {
    useViewerExtensionStore.setState(initialState, true)
  })

  it('renders a contributed overlay', () => {
    useViewerExtensionStore.getState().addContribution({
      kind: 'overlay',
      extensionId: 'ext-a',
      component: () => <div data-testid="overlay-a">A</div>,
    })

    render(<ExtensionOverlayHost />)

    expect(screen.getByTestId('overlay-a')).toBeInTheDocument()
  })

  it('renders an overlay contributed before the host mounted (D3: no "too early" case)', () => {
    useViewerExtensionStore.getState().addContribution({
      kind: 'overlay',
      extensionId: 'early',
      component: () => <div data-testid="overlay-early">early</div>,
    })

    render(<ExtensionOverlayHost />)

    expect(screen.getByTestId('overlay-early')).toBeInTheDocument()
  })

  it('drops an overlay once its extension is withdrawn', () => {
    useViewerExtensionStore.getState().addContribution({
      kind: 'overlay',
      extensionId: 'ext-b',
      component: () => <div data-testid="overlay-b">B</div>,
    })

    const { rerender } = render(<ExtensionOverlayHost />)
    expect(screen.getByTestId('overlay-b')).toBeInTheDocument()

    act(() => {
      useViewerExtensionStore.getState().removeContributionsFor('ext-b')
    })
    rerender(<ExtensionOverlayHost />)

    expect(screen.queryByTestId('overlay-b')).not.toBeInTheDocument()
  })

  it('renders non-overlay contributions from other extensions, untouched', () => {
    useViewerExtensionStore.getState().addContribution({
      kind: 'toolbarEntry',
      extensionId: 'ext-c',
      entry: { id: 'e1', label: 'Entry', icon: () => null, onClick: () => {} },
    })
    useViewerExtensionStore.getState().addContribution({
      kind: 'overlay',
      extensionId: 'ext-d',
      component: () => <div data-testid="overlay-d">D</div>,
    })

    render(<ExtensionOverlayHost />)

    expect(screen.getByTestId('overlay-d')).toBeInTheDocument()
    expect(screen.queryByText('Entry')).not.toBeInTheDocument()
  })

  it('renders nothing when there are no overlay contributions', () => {
    const { container } = render(<ExtensionOverlayHost />)
    expect(container.querySelectorAll('*').length).toBeGreaterThan(0)
    expect(container.textContent).toBe('')
  })
})
