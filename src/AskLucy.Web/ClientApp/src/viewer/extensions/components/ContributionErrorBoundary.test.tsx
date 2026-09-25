import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'
import { ContributionErrorBoundary } from './ContributionErrorBoundary'

const initialState = useViewerExtensionStore.getState()

function Thrower(): never {
  throw new Error('boom')
}

describe('ContributionErrorBoundary (specs/073 contract X7, research D3a)', () => {
  beforeEach(() => {
    useViewerExtensionStore.setState(initialState, true)
    // React logs every caught render error itself; the boundary also logs. Both are expected here.
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders its child unchanged, adding no DOM of its own', () => {
    const { container } = render(
      <ContributionErrorBoundary extensionId="ext-ok">
        <span data-testid="child">ok</span>
      </ContributionErrorBoundary>,
    )

    expect(container.firstElementChild).toBe(screen.getByTestId('child'))
  })

  it('marks the extension failed with the render error and renders nothing for that item', () => {
    const { container } = render(
      <ContributionErrorBoundary extensionId="ext-broken">
        <Thrower />
      </ContributionErrorBoundary>,
    )

    expect(container).toBeEmptyDOMElement()
    expect(useViewerExtensionStore.getState().extensions['ext-broken']).toMatchObject({
      lifecycle: 'failed',
      failureReason: 'Render failed: boom',
    })
  })

  it('leaves a sibling item and the surrounding tree mounted', () => {
    render(
      <div data-testid="parent">
        <ContributionErrorBoundary extensionId="ext-broken">
          <Thrower />
        </ContributionErrorBoundary>
        <ContributionErrorBoundary extensionId="ext-ok">
          <span data-testid="sibling">still here</span>
        </ContributionErrorBoundary>
      </div>,
    )

    expect(screen.getByTestId('parent')).toBeInTheDocument()
    expect(screen.getByTestId('sibling')).toBeInTheDocument()
    expect(useViewerExtensionStore.getState().extensions['ext-ok']).toBeUndefined()
  })
})
