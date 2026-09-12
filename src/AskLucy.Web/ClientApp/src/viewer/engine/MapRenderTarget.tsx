import { Box } from '@mui/material'
import { useEffect, useRef } from 'react'
import type { GoogleMapsGisLayerHandle } from '../layers/gis/GoogleMapsGisLayer'
import { applyCameraViewMode } from '../camera/cameraViewMode'
import { RotationDriver } from '../camera/rotationDriver'
import { useViewerEngineStore } from '../store/viewerEngineStore'
import type { MapStyleId } from '../api/commands'
import { useGoogleMapsStore } from '../store/googleMapsStore'
import { drawingSpaceRegistry } from '../scene/DrawingSpaceRegistry'
import { rendererState } from '../scene/rendererState'
import type { CameraState } from '../api/commands'
import { useThemeStore } from '../../store/themeStore'
import type { ViewerEngine } from './ViewerEngine'

/** specs/051 FR-025 — reads the live camera snapshot from the underlying `google.maps.Map`. All
 * four values already exist on the map instance; this is exposing them, not computing anything
 * new. */
function getCameraStateFromHandle(handle: GoogleMapsGisLayerHandle): CameraState | null {
  const center = handle.map.getCenter?.()
  const zoom = handle.map.getZoom?.()
  if (!center || zoom === undefined) return null
  return {
    latitude: center.lat(),
    longitude: center.lng(),
    heading: handle.map.getHeading?.() ?? 0,
    tilt: handle.map.getTilt?.() ?? 0,
    zoom,
  }
}

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
  // Google's `colorScheme` MapOption "can only be set when the map is initialized"
  // (@types/google.maps) — there is no live `map.setOptions({colorScheme})` path, so the only
  // way to reflect a theme toggle on the map tiles is to recreate the underlying
  // google.maps.Map. Reading the mode via the hook (not a one-off `getState()` inside the
  // effect) makes it part of the effect's own dependency array, below.
  const themeMode = useThemeStore((state) => state.mode)
  // specs/048-buildings-only-map-style research.md Decision 4: on a vector deployment (a base
  // Map ID configured), "Buildings only" comes from switching to a second, cloud-styled Map ID
  // (docs/google-maps-styles/*.cloud.json) rather than a client-side `styles` array, which Google
  // ignores on vector rendering. Map ID (like `colorScheme`) can only be set when a map is
  // initialized, so this — like a theme toggle — recreates the whole map rather than calling a
  // live setter. `effectiveMapId` only changes when `mapStyle` crosses the buildings-only
  // boundary (both env vars are static for the session), so switching between roadmap/satellite/
  // hybrid never triggers this — only entering/leaving buildings-only on a vector deployment does.
  const mapStyle = useViewerEngineStore((state) => state.mapStyle)
  const baseMapId = import.meta.env.VITE_GOOGLE_MAPS_MAP_ID
  const buildingsOnlyMapId = import.meta.env.VITE_GOOGLE_MAPS_BUILDINGS_ONLY_MAP_ID
  const effectiveMapId = mapStyle === 'buildings-only' && buildingsOnlyMapId ? buildingsOnlyMapId : baseMapId
  // Carries the last-known pan/zoom/heading/tilt across a theme-triggered remount so toggling
  // the theme doesn't snap the camera back to this component's original mount-time `center`/
  // `zoom` props, or reset rotation/tilt to the north-up isometric default.
  const lastCameraRef = useRef<{
    latitude: number
    longitude: number
    zoom?: number
    heading?: number
    tilt?: number
  } | null>(null)

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
          mapId: effectiveMapId,
          container,
          center: lastCameraRef.current ?? center,
          zoom: lastCameraRef.current?.zoom ?? zoom,
          heading: lastCameraRef.current?.heading,
          tilt: lastCameraRef.current?.tilt,
          reducedQuality,
          colorScheme: themeMode,
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
      // specs/042-site-boundary-resolution: expose the full handle so SiteBoundaryOverlay can
      // call setSiteBoundary() without knowing about MapRenderTarget's internals.
      useGoogleMapsStore.getState().setHandle(handle)

      // specs/051 research D3a/D3 — routes DrawingSpaceRegistry's onFrame-containment failures
      // and rendererState's requirement conflicts to the real viewer event bus. GoogleMapsGisLayer.ts
      // deliberately does not depend on `viewerEngine` itself, so this is done here instead.
      drawingSpaceRegistry.bindFailureReporter((extensionId, message) =>
        viewerEngine.notifyDrawingCallbackFailed(extensionId, message),
      )
      rendererState.bindConflictReporter((requirement, requestedBy) =>
        viewerEngine.notifyDrawingRequirementConflict(requirement, requestedBy),
      )

      // specs/051 FR-026/research D6 — announces on the map's own 'idle' event (fires once
      // movement settles, not per frame), never a per-frame poll. Optional chaining: the map
      // instance in existing tests is a lightweight stub without Maps SDK event methods.
      const idleListener = handle.map.addListener?.('idle', () => {
        const camera = getCameraStateFromHandle(handle!)
        if (camera) viewerEngine.notifyCameraChanged(camera)
      })

      rotationDriver = new RotationDriver({ setHeading: handle.setHeading }, lastCameraRef.current?.heading)

      // US5 (FR-018): the marker becomes selectable only once it actually exists on the map.
      const unregisterSelectable = viewerEngine.registerSelectableElement(layerId, handle.currentLocationMarkerId)

      let lastAppliedMapStyle: MapStyleId | undefined

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
        // Only touch mapTypeId when the style actually changed — this subscription fires on
        // *every* store mutation (rotation toggling, selection, etc.), and re-issuing
        // setMapTypeId unconditionally lets Google's Maps JS API auto-tilt the camera (its
        // built-in "45° imagery" behavior for satellite/hybrid) even when nothing about the
        // map style changed, silently kicking Plan 2D out to the isometric tilt.
        if (mapStyle !== lastAppliedMapStyle) {
          lastAppliedMapStyle = mapStyle
          handle.setMapTypeId(mapStyle)
          // Google may have just changed tilt as a side effect of the mapTypeId change —
          // reassert the active view mode's tilt so a style switch never changes the view mode.
          applyCameraViewMode(handle, camera.mode)
        }
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
        applyMapStyle: (mapStyle) => {
          if (!handle || mapStyle === lastAppliedMapStyle) return
          lastAppliedMapStyle = mapStyle
          handle.setMapTypeId(mapStyle)
          applyCameraViewMode(handle, useViewerEngineStore.getState().camera.mode)
        },
        getCameraState: () => getCameraStateFromHandle(handle!) ?? { latitude: 0, longitude: 0, heading: 0, tilt: 0, zoom: 0 },
      })

      // Combine the teardown functions into the single `unregister` slot the outer cleanup
      // already calls, rather than tracking extra variables.
      const unregisterRenderTarget = unregister
      unregister = () => {
        unregisterRenderTarget()
        unregisterSelectable()
        if (idleListener) google.maps.event.removeListener(idleListener)
      }
    })()

    return () => {
      cancelled = true
      // Remember the live pan/zoom/heading/tilt before tearing down — read here (not from the
      // closed-over `center`/`zoom` props) so a theme-toggle-triggered remount reopens where the
      // user left off rather than snapping back to this component's original mount position and
      // the north-up isometric default.
      const currentCenter = handle?.map.getCenter?.()
      const currentZoom = handle?.map.getZoom?.()
      const currentHeading = handle?.map.getHeading?.()
      const currentTilt = handle?.map.getTilt?.()
      if (currentCenter) {
        lastCameraRef.current = {
          latitude: currentCenter.lat(),
          longitude: currentCenter.lng(),
          zoom: currentZoom,
          heading: currentHeading,
          tilt: currentTilt,
        }
      }
      unsubscribeStore?.()
      unregister?.()
      rotationDriver?.dispose()
      handle?.dispose()
      useGoogleMapsStore.getState().setMap(null)
      useGoogleMapsStore.getState().setHandle(null)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [layerId, themeMode, effectiveMapId])

  return <Box ref={containerRef} data-testid="viewer-map" sx={{ position: 'absolute', inset: 0 }} />
}
