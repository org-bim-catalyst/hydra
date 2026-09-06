import { act, render } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { SiteBoundaryOverlay } from './SiteBoundaryOverlay'
import { useActiveSiteBoundaryStore } from '../../../store/activeSiteBoundaryStore'
import { useGoogleMapsStore } from '../../../viewer/store/googleMapsStore'
import type { GoogleMapsGisLayerHandle } from '../../../viewer/layers/gis/GoogleMapsGisLayer'

function fakeHandle(): GoogleMapsGisLayerHandle {
  return {
    map: {} as google.maps.Map,
    scene: {} as never,
    currentLocationMarkerId: 'x',
    panTo: vi.fn(),
    fitBounds: vi.fn(),
    zoomToAltitude: vi.fn(),
    zoomBy: vi.fn(),
    setHeading: vi.fn(),
    setTilt: vi.fn(),
    setMapTypeId: vi.fn(),
    setMarkerHighlighted: vi.fn(),
    setSiteBoundary: vi.fn(),
    dispose: vi.fn(),
  }
}

const sampleBoundary = {
  siteName: 'Al Safa Park 2',
  centroid: { latitude: 25.156, longitude: 55.2218 },
  polygon: [
    { latitude: 25.156, longitude: 55.221 },
    { latitude: 25.156, longitude: 55.222 },
    { latitude: 25.155, longitude: 55.222 },
  ],
  areaSquareMeters: 15000,
  confidence: 0.92,
  confidenceLevel: 'high' as const,
  source: 'OsmBoundary' as const,
  sourceDetail: 'OpenStreetMap (leisure=park)',
  alternativeCandidateNames: [],
}

describe('SiteBoundaryOverlay', () => {
  afterEach(() => {
    useActiveSiteBoundaryStore.getState().clearBoundary()
    useGoogleMapsStore.getState().setHandle(null)
  })

  it('renders nothing (purely imperative, like POIMarkerOverlay)', () => {
    const { container } = render(<SiteBoundaryOverlay />)
    expect(container).toBeEmptyDOMElement()
  })

  it('does nothing when the map handle is not yet ready', () => {
    render(<SiteBoundaryOverlay />)
    act(() => useActiveSiteBoundaryStore.getState().setBoundary(sampleBoundary))
    // No handle to assert against — just confirming no throw.
  })

  it('calls handle.setSiteBoundary once a boundary and a handle are both available', () => {
    const handle = fakeHandle()
    act(() => useGoogleMapsStore.getState().setHandle(handle))
    render(<SiteBoundaryOverlay />)

    act(() => useActiveSiteBoundaryStore.getState().setBoundary(sampleBoundary))

    expect(handle.setSiteBoundary).toHaveBeenCalledWith({
      exteriorRing: sampleBoundary.polygon,
      confidenceLevel: 'high',
    })
  })

  it('clears the boundary (edge case: a new, unrelated site must not leave the old one overlaid)', () => {
    const handle = fakeHandle()
    act(() => useGoogleMapsStore.getState().setHandle(handle))
    render(<SiteBoundaryOverlay />)

    act(() => useActiveSiteBoundaryStore.getState().setBoundary(sampleBoundary))
    act(() => useActiveSiteBoundaryStore.getState().clearBoundary())

    expect(handle.setSiteBoundary).toHaveBeenLastCalledWith(null)
  })
})
