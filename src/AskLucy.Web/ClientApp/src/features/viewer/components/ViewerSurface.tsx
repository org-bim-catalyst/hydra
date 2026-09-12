import { Box } from '@mui/material'
import { useEffect, useRef } from 'react'
import { useWebGLSupport } from '../../../hooks/useWebGLSupport'
import { useViewerEngineStore } from '../../../viewer/store/viewerEngineStore'
import { PlaceholderRenderTarget } from '../../../viewer/engine/PlaceholderRenderTarget'
import { ViewerFallback } from '../../../viewer/engine/ViewerFallback'
import { MapRenderTarget } from '../../../viewer/engine/MapRenderTarget'
import { viewerEngine } from '../../../viewer/engine/viewerEngineInstance'
import { ContentLoadingIndicator } from '../../../viewer/content/components/ContentLoadingIndicator'
import { useContentStore } from '../../../viewer/content/contentStore'
import { ExtensionFailureNotice } from '../../../viewer/extensions/components/ExtensionFailureNotice'
import { ExtensionOverlayHost } from '../../../viewer/extensions/components/ExtensionOverlayHost'
import { ExtensionToolbar } from '../../../viewer/extensions/components/ExtensionToolbar'
import { DECLARED_EXTENSIONS } from '../../../viewer/extensions/declared'
import { viewerExtensionLoader } from '../../../viewer/extensions/loader'
import { panelTypeRegistry } from '../../../viewer/panels/registry'
import { useFloatingPanelStore } from '../../../viewer/panels/store/floatingPanelStore'
import { useActiveLocationStore } from '../../../store/activeLocationStore'

const DEFAULT_MAP_ZOOM = 15

// specs/038-viewer-poi-zoom: fallback altitude table when viewport is absent. Module-level (not
// recreated in the component body) so the useEffect below that reads it doesn't need it in its
// dependency array — a fresh object literal every render would otherwise either be a missing-dep
// lint warning or, if added, re-run the effect on every render.
const LOCATION_TYPE_ALTITUDE: Record<string, number> = {
  ROOFTOP: 200,
  RANGE_INTERPOLATED: 200,
  GEOMETRIC_CENTER: 800,
  APPROXIMATE: 8000,
}
const DEFAULT_ALTITUDE = 2000

declare global {
  interface Window {
    __askLucyFloatingPanelStore?: typeof useFloatingPanelStore
    __askLucyPanelTypeRegistry?: typeof panelTypeRegistry
  }
}

// spec 028 contracts/panel-type-registry.md "Verification" — lets a developer open/inspect panels
// and register a brand-new type directly from the browser devtools console, proving the
// registry/store work end-to-end with zero AI-agent code involved (SC-006), mirroring spec 027's
// `window.__askLucyViewerEngine` exposure. Development builds only — never shipped to production
// (constitution §8).
if (import.meta.env.DEV && typeof window !== 'undefined') {
  window.__askLucyFloatingPanelStore = useFloatingPanelStore
  window.__askLucyPanelTypeRegistry = panelTypeRegistry
}

/** FR-001: the viewer's full-viewport mount point and primary workspace surface. Reads the
 * active location from `activeLocationStore` (specs/036-startup-geolocation) — no longer a
 * prop — so both startup geolocation and agent-confirmed locations (specs/035, spec 036 US3)
 * drive the viewer through the same shared store. */
export function ViewerSurface() {
  const supportsWebGL = useWebGLSupport()
  const contentMode = useViewerEngineStore((s) => s.contentMode)
  // specs/051 FR-003/research D1: the map is the viewer's own first, default content, loaded
  // through `engine.loadContent(...)` rather than a raw `addLayer` call — this ref remembers the
  // content id `loadContent` generated, since revert-to-placeholder needs it to unload cleanly.
  // The RenderLayer id downstream consumers (`MapRenderTarget`'s `layerId` prop) need is derived
  // below from the content store, reactively, rather than mirrored into separate React state
  // (calling `setState` synchronously inside an effect is exactly the anti-pattern that would
  // introduce — the content store update `loadContent` already makes is itself the state).
  const mapContentIdRef = useRef<string | null>(null)
  const mapLayerId = useContentStore((s) => s.content.find((c) => c.id === mapContentIdRef.current)?.layerId ?? null)

  // spec.md Edge Cases: shared by "location became unavailable" (FR-012) and "the map/GIS
  // provider is unreachable" — both revert to the placeholder the same way.
  function revertToPlaceholder() {
    if (mapContentIdRef.current) {
      viewerEngine.unloadContent(mapContentIdRef.current)
      mapContentIdRef.current = null
    }
    useViewerEngineStore.getState().setContentMode('placeholder')
  }

  // FR-002/research D3: starts every declared extension on mount. Not awaited as a group
  // (research D4) so a slow one cannot delay first paint. FR-028: stops every declared extension
  // and withdraws every contribution when the viewer closes — the loader's own idempotency rules
  // (FR-008, FR-010) are what make this safe under React 19 Strict Mode's mount→cleanup→remount.
  useEffect(() => {
    for (const id of DECLARED_EXTENSIONS) {
      void viewerExtensionLoader.start(id)
    }
    return () => {
      for (const id of DECLARED_EXTENSIONS) {
        void viewerExtensionLoader.stop(id)
      }
    }
  }, [])

  // specs/036-startup-geolocation: read from shared active location store.
  // source !== null means a location is set (either from device or agent); null means no location.
  const source = useActiveLocationStore((s) => s.source)
  const latitude = useActiveLocationStore((s) => s.latitude)
  const longitude = useActiveLocationStore((s) => s.longitude)
  // specs/038-viewer-poi-zoom: viewport and locationType drive altitude-accurate zoom.
  const viewport = useActiveLocationStore((s) => s.viewport)
  const locationType = useActiveLocationStore((s) => s.locationType)

  useEffect(() => {
    const store = useViewerEngineStore.getState()

    if (source !== null && latitude !== null && longitude !== null) {
      const center = { latitude, longitude }
      // FR-007: replaces the placeholder as the active view once a location is set. Only added
      // once — a coordinate update (user physically moved, or agent confirmed a new location)
      // just re-centers via zoomToLocation below, it doesn't re-add the layer.
      if (store.contentMode !== 'map') {
        const result = viewerEngine.loadContent({ kind: 'gis', provider: 'google-maps', center, zoom: DEFAULT_MAP_ZOOM })
        if (result.ok && result.data) {
          mapContentIdRef.current = result.data.contentId
        }
        useViewerEngineStore.getState().setContentMode('map')
      }
      // specs/038-viewer-poi-zoom: priority — fitBounds > zoomToAltitude > legacy zoomToLocation.
      if (viewport !== null) {
        viewerEngine.fitBounds(
          { lat: viewport.northeastLat, lng: viewport.northeastLng },
          { lat: viewport.southwestLat, lng: viewport.southwestLng },
        )
      } else if (locationType !== null) {
        viewerEngine.zoomToAltitude(LOCATION_TYPE_ALTITUDE[locationType] ?? DEFAULT_ALTITUDE)
      } else {
        viewerEngine.zoomToLocation(center.latitude, center.longitude, DEFAULT_MAP_ZOOM)
      }
    } else if (source === null && store.contentMode === 'map') {
      // FR-012: location became unavailable after the map was already active (e.g. permission
      // revoked mid-session) — revert to the placeholder. When no location was ever set,
      // contentMode is already 'placeholder' and this branch is never reached.
      revertToPlaceholder()
    }
  }, [source, latitude, longitude, viewport, locationType])

  return (
    <Box sx={{ position: 'absolute', inset: 0, zIndex: 0, overflow: 'hidden' }}>
      {!supportsWebGL ? (
        <ViewerFallback />
      ) : contentMode === 'map' && latitude !== null && longitude !== null && mapLayerId !== null ? (
        <MapRenderTarget
          viewerEngine={viewerEngine}
          layerId={mapLayerId}
          center={{ latitude, longitude }}
          zoom={DEFAULT_MAP_ZOOM}
          onError={revertToPlaceholder}
        />
      ) : (
        <PlaceholderRenderTarget />
      )}
      <ExtensionOverlayHost />
      {/* research D6/T036 — top-right: clear of the weather widget and boundary confidence
          badge (top-left) and the panel-hub indicator (bottom-left). Renders nothing when no
          extension has contributed an entry (FR-023). */}
      <ExtensionToolbar />
      <ExtensionFailureNotice />
      <ContentLoadingIndicator />
    </Box>
  )
}
