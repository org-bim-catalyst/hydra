import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { FEATURE_SELECTED_EVENT } from './SpatialLayersOverlay'
import { SpatialLayersOverlay } from './SpatialLayersOverlay'
import { disposeSpatialLayerOverlays } from './spatialLayerOverlayLifecycle'
import { toSpatialMapFeatureGeometry } from './spatialLayerFeatureGeometry'
import { useGoogleMapsStore } from '../../../viewer/store/googleMapsStore'

describe('SpatialLayersOverlay', () => {
  afterEach(() => {
    cleanup()
    useGoogleMapsStore.getState().setMap(null)
    vi.unstubAllGlobals()
  })

  it('maps point, linestring, and closed polygon result geometry to viewer primitives', () => {
    const feature = (kind: 'point' | 'linestring' | 'polygon', coordinates: Array<[number, number]>) => ({
      featureId: kind, name: kind, attributes: {},
      geometry: { kind, points: coordinates.map(([latitude, longitude]) => ({ latitude, longitude })) },
    })
    expect(toSpatialMapFeatureGeometry(feature('point', [[23, 58]]))).toEqual({ kind: 'point', points: [{ lat: 23, lng: 58 }] })
    expect(toSpatialMapFeatureGeometry(feature('linestring', [[23, 58], [24, 59]]))).toEqual({
      kind: 'linestring', points: [{ lat: 23, lng: 58 }, { lat: 24, lng: 59 }],
    })
    expect(toSpatialMapFeatureGeometry(feature('polygon', [[23, 58], [23, 59], [24, 59], [23, 58]]))).toEqual({
      kind: 'polygon', points: [{ lat: 23, lng: 58 }, { lat: 23, lng: 59 }, { lat: 24, lng: 59 }, { lat: 23, lng: 58 }],
    })
    expect(toSpatialMapFeatureGeometry(feature('polygon', [[23, 58], [23, 59], [24, 59]]))).toBeNull()
  })

  it('selects and disposes map primitives for independently managed overlay features', () => {
    const listeners = new Map<string, () => void>()
    const marker = {
      map: {},
      addListener: (event: string, callback: () => void) => listeners.set(event, callback),
    }
    const polygon = { setMap: vi.fn() }
    const line = { setMap: vi.fn() }
    const selected = vi.fn()
    document.addEventListener(FEATURE_SELECTED_EVENT, selected)

    marker.addListener('click', () => {
      document.dispatchEvent(new CustomEvent(FEATURE_SELECTED_EVENT, { detail: { layerId: 'overlay-a' } }))
    })
    listeners.get('click')?.()
    disposeSpatialLayerOverlays([marker as never], [polygon as never], [line as never])

    expect(selected).toHaveBeenCalledOnce()
    expect(marker.map).toBeNull()
    expect(polygon.setMap).toHaveBeenCalledWith(null)
    expect(line.setMap).toHaveBeenCalledWith(null)
    document.removeEventListener(FEATURE_SELECTED_EVENT, selected)
  })

  it('shows a visible error when map overlay setup fails', async () => {
    useGoogleMapsStore.getState().setMap({} as google.maps.Map)
    vi.stubGlobal('google', { maps: { importLibrary: vi.fn().mockRejectedValue(new Error('Map library unavailable')) } })

    render(<SpatialLayersOverlay />)

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Map library unavailable'))
  })
})
