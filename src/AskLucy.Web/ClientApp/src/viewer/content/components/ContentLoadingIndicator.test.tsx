import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { useContentStore } from '../contentStore'
import { ContentLoadingIndicator } from './ContentLoadingIndicator'
import type { ViewerContent } from '../ViewerContent'

const initialState = useContentStore.getState()

function makeContent(overrides: Partial<ViewerContent> = {}): ViewerContent {
  return {
    id: 'c1',
    layerId: 'l1',
    source: { kind: 'gis', provider: 'google-maps', center: { latitude: 0, longitude: 0 } },
    placement: null,
    loadState: 'loading',
    failureReason: null,
    ...overrides,
  }
}

describe('ContentLoadingIndicator (FR-005)', () => {
  beforeEach(() => {
    useContentStore.setState(initialState, true)
  })

  it('renders nothing when nothing is loading', () => {
    render(<ContentLoadingIndicator />)
    expect(screen.queryByTestId('content-loading-indicator')).not.toBeInTheDocument()
  })

  it('shows a singular message for one loading item', () => {
    useContentStore.getState().upsert(makeContent())
    render(<ContentLoadingIndicator />)
    expect(screen.getByTestId('content-loading-indicator')).toHaveTextContent('Loading content…')
  })

  it('shows a count for multiple loading items, and ignores loaded/failed ones', () => {
    useContentStore.getState().upsert(makeContent({ id: 'a' }))
    useContentStore.getState().upsert(makeContent({ id: 'b' }))
    useContentStore.getState().upsert(makeContent({ id: 'c', loadState: 'loaded' }))

    render(<ContentLoadingIndicator />)
    expect(screen.getByTestId('content-loading-indicator')).toHaveTextContent('Loading 2 items…')
  })
})
