import { Box } from '@mui/material'
import { useEffect, useRef } from 'react'
import type { GoogleMapsGisLayerHandle } from '../layers/gis/GoogleMapsGisLayer'
import { applyCameraViewMode } from '../camera/cameraViewMode'
import { RotationDriver } from '../camera/rotationDriver'
import { useViewerEngineStore } from '../store/viewerEngineStore'
import { useGoogleMapsStore } from '../store/googleMapsStore'
import type { ViewerEngine } from './ViewerEngine'

export interface MapRenderTargetProps {
  viewerEngine: ViewerEngine
  layerId: string
  center: { latitude: number; longitude: number }
  zoom?: number
  /** spec.md Edge Cases — "the map/GIS provider is unreachable... MUST remain on (or fall back
   * to) the placeholder background rather than showing a broken or empty map." Called for a
   * missing/invalid API key or any failure loading Google Maps — never left as a blank,
   * indefinitely-empty container. */
  onError: () => void
}

/** The viewer's map/GIS content mode (FR-007, User Story 2) — mounts the Google Maps `<div>` and
 * bridges it to a Three.js scene via `createGoogleMapsGisLayer` (research.md Decision 3), loaded
 * lazily (dynamic `import()`, T038) so the Maps loader/Three.js-bridging code never ships in the
 * initial route bundle (constitution §15). Registers itself as the viewer engine's active render
 * target so `zoomToLocation`/`setViewMode`/`setRotationEnabled` commands reach it (User Story 3),
 * and drives continuous rotation via `RotationDriver` while `viewerEngineStore.camera.rotationEnabled`
 * is true and the device isn't flagged for reduced quality (T032a). */
export function MapRenderTarget({ viewerEngine, layerId, center, zoom, onError }: MapRenderTargetProps) {
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const container = containerRef.current
    if (!container) return

    let handle: GoogleMapsGisLayerHandle | undefined
    let rotationDriver: RotationDriver | undefined
    let unregister: (() => void) | undefined
    let unsubscribeStore: (() => void) | undefined
    let reducedQuality = false
    let cancelled = false

    const apiKey = import.meta.env.VITE_GOOGLE_MAPS_API_KEY
    if (!apiKey) {
      // Missing configuration is treated the same as a provider failure (spec.md Edge Cases) —
      // logged loudly for the developer, but the *user* still gets a coherent placeholder, not
      // a blank screen.
      console.error(
        'VITE_GOOGLE_MAPS_API_KEY is not set — the map/GIS content mode cannot load. See .env.example.',
      )
      onError()
      return
    }

    void (async () => {
      try {
        const { createGoogleMapsGisLayer, shouldReduceMapQuality } = await import(
          '../layers/gis/GoogleMapsGisLayer'
        )
        if (cancelled) return

        reducedQuality = shouldReduceMapQuality()
        handle = await createGoogleMapsGisLayer({
          apiKey,
          mapId: import.meta.env.VITE_GOOGLE_MAPS_MAP_ID,
          container,
          center,
          zoom,
          reducedQuality,
          onLoaded: () => viewerEngine.notifyContentLoaded(layerId),
        })
      } catch (error) {
        // spec.md Edge Cases — never leaves an unhandled rejection, and never leaves the
        // viewer showing a blank/broken map; falls back to the placeholder instead.
        console.error('Failed to load the map/GIS content mode.', error)
        if (!cancelled) onError()
        return
      }
      if (cancelled) {
        handle.dispose()
        return
      }

      // specs/038-viewer-poi-zoom: expose the live map to POIMarkerOverlay via the shared store.
      useGoogleMapsStore.getState().setMap(handle.map)

      rotationDriver = new RotationDriver({ setHeading: handle.setHeading })

      // US5 (FR-018): the marker becomes selectable only once it actually exists on the map.
      const unregisterSelectable = viewerEngine.registerSelectableElement(layerId, handle.currentLocationMarkerId)

      const applyStoreState = () => {
        if (!handle || !rotationDriver) return
        const { camera, selection, mapStyle } = useViewerEngineStore.getState()
        applyCameraViewMode(handle, camera.mode)
        // T032a: a device already flagged for reduced quality never auto-rotates, regardless
        // of the stored preference — one consistent signal driving both concerns.
        rotationDriver.setEnabled(camera.rotationEnabled && !reducedQuality)
        handle.setMarkerHighlighted(
          selection.selectedLayerId === layerId && selection.selectedElementId === handle.currentLocationMarkerId,
        )
        handle.setMapTypeId(mapStyle)
      }

      applyStoreState()
      unsubscribeStore = useViewerEngineStore.subscribe(applyStoreState)

      unregister = viewerEngine.registerRenderTarget({
        panTo: handle.panTo,
        fitBounds: handle.fitBounds,
        zoomToAltitude: handle.zoomToAltitude,
        zoomBy: handle.zoomBy,
        applyViewMode: (mode) => applyCameraViewMode(handle!, mode),
        applyRotationEnabled: (enabled) => rotationDriver?.setEnabled(enabled && !reducedQuality),
        applyMapStyle: (mapStyle) => handle?.setMapTypeId(mapStyle),
      })

      // Combine the two teardown functions into the single `unregister` slot the outer cleanup
      // already calls, rather than tracking a third variable.
      const unregisterRenderTarget = unregister
      unregister = () => {
        unregisterRenderTarget()
        unregisterSelectable()
      }
    })()

    return () => {
      cancelled = true
      unsubscribeStore?.()
      unregister?.()
      rotationDriver?.dispose()
      handle?.dispose()
      useGoogleMapsStore.getState().setMap(null)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [layerId])

  return <Box ref={containerRef} data-testid="viewer-map" sx={{ position: 'absolute', inset: 0 }} />
}
