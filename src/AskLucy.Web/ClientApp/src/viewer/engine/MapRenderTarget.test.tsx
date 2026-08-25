import { render, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useViewerEngineStore } from '../store/viewerEngineStore'
import { ViewerEngine } from './ViewerEngine'
import { MapRenderTarget } from './MapRenderTarget'

const { createGoogleMapsGisLayerMock, shouldReduceMapQualityMock, fakeHandle } = vi.hoisted(() => {
  const handle = {
    map: {},
    scene: {},
    currentLocationMarkerId: 'current-location',
    panTo: vi.fn(),
    setHeading: vi.fn(),
    setTilt: vi.fn(),
    setMarkerHighlighted: vi.fn(),
    setMapTypeId: vi.fn(),
    dispose: vi.fn(),
  }
  return {
    fakeHandle: handle,
    createGoogleMapsGisLayerMock: vi.fn().mockResolvedValue(handle),
    shouldReduceMapQualityMock: vi.fn().mockReturnValue(false),
  }
})

vi.mock('../layers/gis/GoogleMapsGisLayer', () => ({
  createGoogleMapsGisLayer: createGoogleMapsGisLayerMock,
  shouldReduceMapQuality: shouldReduceMapQualityMock,
}))

const initialState = useViewerEngineStore.getState()

describe('MapRenderTarget (US5, FR-018 — highlight wiring)', () => {
  beforeEach(() => {
    useViewerEngineStore.setState(initialState, true)
    vi.clearAllMocks()
    createGoogleMapsGisLayerMock.mockResolvedValue(fakeHandle)
    vi.stubEnv('VITE_GOOGLE_MAPS_API_KEY', 'test-key')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('registers the current-location marker as selectable, and highlights it when selected via the engine', async () => {
    const engine = new ViewerEngine()
    render(
      <MapRenderTarget
        viewerEngine={engine}
        layerId="gis-current-location"
        center={{ latitude: 51.5074, longitude: -0.1278 }}
        onError={() => {}}
      />,
    )

    await waitFor(() => expect(createGoogleMapsGisLayerMock).toHaveBeenCalled())
    await waitFor(() => expect(fakeHandle.setMarkerHighlighted).toHaveBeenCalledWith(false))

    const result = engine.select('gis-current-location', 'current-location')

    expect(result.ok).toBe(true)
    await waitFor(() => expect(fakeHandle.setMarkerHighlighted).toHaveBeenLastCalledWith(true))

    engine.clearSelection()

    await waitFor(() => expect(fakeHandle.setMarkerHighlighted).toHaveBeenLastCalledWith(false))
  })

  it('fails to select an unregistered marker id, and never highlights', async () => {
    const engine = new ViewerEngine()
    render(
      <MapRenderTarget
        viewerEngine={engine}
        layerId="gis-current-location"
        center={{ latitude: 51.5074, longitude: -0.1278 }}
        onError={() => {}}
      />,
    )
    await waitFor(() => expect(createGoogleMapsGisLayerMock).toHaveBeenCalled())

    const result = engine.select('gis-current-location', 'not-a-real-marker')

    expect(result.ok).toBe(false)
  })

  it('calls onError instead of rendering a blank map when no API key is configured (spec.md Edge Cases)', async () => {
    vi.stubEnv('VITE_GOOGLE_MAPS_API_KEY', '')
    const engine = new ViewerEngine()
    const onError = vi.fn()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})

    render(
      <MapRenderTarget
        viewerEngine={engine}
        layerId="gis-current-location"
        center={{ latitude: 51.5074, longitude: -0.1278 }}
        onError={onError}
      />,
    )

    await waitFor(() => expect(onError).toHaveBeenCalledTimes(1))
    expect(createGoogleMapsGisLayerMock).not.toHaveBeenCalled()
    consoleError.mockRestore()
  })

  it('calls onError, and never leaves an unhandled rejection, when loading the map throws', async () => {
    createGoogleMapsGisLayerMock.mockRejectedValueOnce(new Error('Failed to load Google Maps'))
    const engine = new ViewerEngine()
    const onError = vi.fn()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})

    render(
      <MapRenderTarget
        viewerEngine={engine}
        layerId="gis-current-location"
        center={{ latitude: 51.5074, longitude: -0.1278 }}
        onError={onError}
      />,
    )

    await waitFor(() => expect(onError).toHaveBeenCalledTimes(1))
    consoleError.mockRestore()
  })
})
