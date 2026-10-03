import { act, cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { useViewerEngineStore } from '../../../viewer/store/viewerEngineStore'
import { MapTransitionOverlay } from './MapTransitionOverlay'

afterEach(() => {
  cleanup()
  useViewerEngineStore.setState({ mapTransition: null })
})

describe('MapTransitionOverlay', () => {
  it('shows nothing while the map is idle', () => {
    const { container } = render(<MapTransitionOverlay />)
    expect(container).toBeEmptyDOMElement()
  })

  it('blurs the viewer and says what the map is doing, until that transition ends', () => {
    render(<MapTransitionOverlay />)

    let id = 0
    act(() => {
      id = useViewerEngineStore.getState().beginMapTransition('Opening the outline editor...')
    })
    expect(screen.getByRole('status')).toHaveTextContent('Opening the outline editor...')

    act(() => useViewerEngineStore.getState().endMapTransition(id))
    expect(screen.queryByTestId('map-transition-overlay')).not.toBeInTheDocument()
  })

  it('an older transition ending does not hide a newer one', () => {
    render(<MapTransitionOverlay />)

    act(() => {
      const first = useViewerEngineStore.getState().beginMapTransition('Changing the map style...')
      useViewerEngineStore.getState().beginMapTransition('Opening the outline editor...')
      useViewerEngineStore.getState().endMapTransition(first)
    })

    expect(screen.getByRole('status')).toHaveTextContent('Opening the outline editor...')
  })
})
