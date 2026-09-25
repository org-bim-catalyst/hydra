import { Box } from '@mui/material'
import { useEffect } from 'react'
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
import { viewerSession } from '../../../viewer/session/viewerSession'
// Side-effect imports: the subscription that keeps the scene's reference point on the active
// location, and the sign-out subscription that ends the viewer session.
import '../../../viewer/session/anchorFollowsActiveLocation'
import '../../../viewer/session/resetViewerSession'
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
  // through `engine.loadContent(...)` rather than a raw `addLayer` call. Its content id lives in
  // `viewerSession`, not a component ref, so returning to this route re-attaches to the map that
  // is still loaded instead of losing track of it (see viewerSession.ts).
  const mapLayerId = useContentStore((s) => s.content.find((c) => c.id === viewerSession.mapContentId)?.layerId ?? null)

  // spec.md Edge Cases: shared by "location became unavailable" (FR-012) and "the map/GIS
  // provider is unreachable" — both revert to the placeholder the same way.
  function revertToPlaceholder() {
    if (viewerSession.mapContentId) {
      viewerEngine.unloadContent(viewerSession.mapContentId)
      viewerSession.mapContentId = null
    }
    useViewerEngineStore.getState().setContentMode('placeholder')
  }

  // FR-002/research D3: starts every declared extension on mount. Not awaited as a group
  // (research D4) so a slow one cannot delay first paint. Start is idempotent (FR-008), so a
  // remount — returning from another route, or React 19 Strict Mode's double mount — reuses the
  // extensions already running rather than restarting them.
  //
  // Deliberately no stop on unmount: stopping withdrew every contribution, so leaving /studio for
  // another page closed the live panels' kinds, released the drawing spaces and deactivated any
  // running analysis — the user came back to a reset workspace. The session now ends on sign-out
  // instead (resetViewerSession.ts).
  useEffect(() => {
    for (const id of DECLARED_EXTENSIONS) {
      void viewerExtensionLoader.start(id)
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

  // Content lifecycle only — this effect never moves the camera. `useGeolocation`'s watchPosition
  // runs for the whole session to detect revocation, so coordinates keep arriving with a few
  // metres of GPS drift long after the page settled. Acting on them here dragged the camera off
  // wherever the user had put it, every time one landed. The camera belongs to the user; a fresh
  // reading of the place already shown is not a reason to take it back.
  useEffect(() => {
    const store = useViewerEngineStore.getState()

    if (source !== null && latitude !== null && longitude !== null) {
      // FR-007: replaces the placeholder as the active view once a location is set. Only added
      // once — a coordinate update doesn't re-add the layer. Also loads when the mode says 'map'
      // but no map content is tracked, so a mismatch can never strand the user on the placeholder.
      if (store.contentMode !== 'map' || !viewerSession.mapContentId) {
        const center = { latitude, longitude }
        const result = viewerEngine.loadContent({ kind: 'gis', provider: 'google-maps', center, zoom: DEFAULT_MAP_ZOOM })
        if (result.ok && result.data) {
          viewerSession.mapContentId = result.data.contentId
        }
        useViewerEngineStore.getState().setContentMode('map')
      }
    } else if (source === null && store.contentMode === 'map') {
      // FR-012: location became unavailable after the map was already active (e.g. permission
      // revoked mid-session) — revert to the placeholder. When no location was ever set,
      // contentMode is already 'placeholder' and this branch is never reached.
      revertToPlaceholder()
    }
  }, [source, latitude, longitude])

  // Identifies a *deliberately* established location, as opposed to passive tracking of the one
  // already shown. The device establishes a location once and then keeps reporting it, so every
  // geolocation fix shares one key; the agent naming a place is a deliberate act every time, so
  // its coordinates and framing hints are all part of its key.
  const framingKey =
    source === null
      ? null
      : source === 'geolocation'
        ? 'geolocation'
        : `agent:${latitude},${longitude},${locationType ?? ''},${
            viewport === null
              ? ''
              : `${viewport.northeastLat},${viewport.northeastLng},${viewport.southwestLat},${viewport.southwestLng}`
          }`

  // Camera framing, deliberately separate from the content effect above: it answers "a new place
  // is being shown, put the camera where that place is visible", which is not what a fresh reading
  // of the place already shown calls for. Coordinates are read at call time rather than depended
  // on, so drift cannot re-trigger it, and the last framed key lives on the session so returning
  // to /studio re-frames nothing the user has since adjusted.
  useEffect(() => {
    if (framingKey === null || framingKey === viewerSession.framedLocationKey) return
    const location = useActiveLocationStore.getState()
    if (location.latitude === null || location.longitude === null) return
    viewerSession.framedLocationKey = framingKey

    // specs/038-viewer-poi-zoom: priority — fitBounds > zoomToAltitude > legacy zoomToLocation.
    if (location.viewport !== null) {
      viewerEngine.fitBounds(
        { lat: location.viewport.northeastLat, lng: location.viewport.northeastLng },
        { lat: location.viewport.southwestLat, lng: location.viewport.southwestLng },
      )
    } else if (location.locationType !== null) {
      viewerEngine.zoomToAltitude(LOCATION_TYPE_ALTITUDE[location.locationType] ?? DEFAULT_ALTITUDE)
    } else {
      viewerEngine.zoomToLocation(location.latitude, location.longitude, DEFAULT_MAP_ZOOM)
    }
  }, [framingKey])

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
      {/* research D6/T036 — top-right: clear of the studio's top-left HUD row (specs/073: Home,
          title, weather, boundary confidence) and the panel-hub indicator (bottom-left). Renders nothing when no
          extension has contributed an entry (FR-023). */}
      <ExtensionToolbar />
      <ExtensionFailureNotice />
      <ContentLoadingIndicator />
    </Box>
  )
}
