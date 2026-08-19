import { Box } from '@mui/material'
import { useEffect } from 'react'
import { useWebGLSupport } from '../../../hooks/useWebGLSupport'
import { useViewerEngineStore } from '../../../viewer/store/viewerEngineStore'
import { PlaceholderRenderTarget } from '../../../viewer/engine/PlaceholderRenderTarget'
import { ViewerFallback } from '../../../viewer/engine/ViewerFallback'
import { MapRenderTarget } from '../../../viewer/engine/MapRenderTarget'
import { viewerEngine } from '../../../viewer/engine/viewerEngineInstance'
import { FloatingPanelHost } from '../../../viewer/panels/components/FloatingPanelHost'
import { useFloatingPanelHub } from '../../../viewer/panels/hooks/useFloatingPanelHub'
import { panelTypeRegistry } from '../../../viewer/panels/registry'
import { useFloatingPanelStore } from '../../../viewer/panels/store/floatingPanelStore'
import '../../../viewer/panels/types'
import type { GeolocationState } from '../hooks/useGeolocation'

const GIS_CURRENT_LOCATION_LAYER_ID = 'gis-current-location'
const DEFAULT_MAP_ZOOM = 15

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

export interface ViewerSurfaceProps {
  geolocation: GeolocationState
}

/** FR-001: the viewer's full-viewport mount point and primary workspace surface, replacing the
 * old `WorkspaceSurface` gradient (features/chat/components/WorkspaceSurface.tsx, research.md
 * Decision 1). Uses the shared `viewerEngine` singleton and switches between the non-interactive
 * WebGL fallback (FR-005), the placeholder (FR-004), and the map/GIS content mode (FR-007) based
 * on `viewerEngineStore.contentMode`. Takes `geolocation` (FR-006) as a prop, lifted to
 * `ChatPage` and shared with `LocationWeatherWidget`, rather than each calling `useGeolocation`
 * independently and opening two redundant `navigator.geolocation.watchPosition` subscriptions. */
/** spec.md Edge Cases: shared by "location became unavailable" (FR-012) and "the map/GIS
 * provider is unreachable" — both revert to the placeholder the same way. */
function revertToPlaceholder() {
  viewerEngine.removeLayer(GIS_CURRENT_LOCATION_LAYER_ID)
  useViewerEngineStore.getState().setContentMode('placeholder')
}

export function ViewerSurface({ geolocation }: ViewerSurfaceProps) {
  const supportsWebGL = useWebGLSupport()
  const contentMode = useViewerEngineStore((s) => s.contentMode)
  useFloatingPanelHub()

  useEffect(() => {
    const store = useViewerEngineStore.getState()

    if (geolocation.status === 'granted' && geolocation.latitude !== null && geolocation.longitude !== null) {
      const center = { latitude: geolocation.latitude, longitude: geolocation.longitude }
      // FR-007: replaces the placeholder as the active view once resolved. Only added once —
      // a later coordinate update (the user physically moving) just re-centers via
      // zoomToLocation below, it doesn't re-add the layer.
      if (store.contentMode !== 'map') {
        viewerEngine.addLayer({
          id: GIS_CURRENT_LOCATION_LAYER_ID,
          kind: 'gis',
          metadata: { provider: 'google-maps', center, zoom: DEFAULT_MAP_ZOOM },
        })
        useViewerEngineStore.getState().setContentMode('map')
      }
      viewerEngine.zoomToLocation(center.latitude, center.longitude, DEFAULT_MAP_ZOOM)
    } else if (geolocation.status === 'unavailable' && store.contentMode === 'map') {
      // FR-012: location became unavailable after the map was already active (e.g. permission
      // revoked mid-session) — revert to the placeholder. When geolocation was never granted in
      // the first place (FR-008/FR-034), contentMode is already 'placeholder' and this is a
      // no-op: no map/layer command is ever issued, matching the graceful-hidden-fallback path.
      revertToPlaceholder()
    }
  }, [geolocation.status, geolocation.latitude, geolocation.longitude])

  return (
    <Box sx={{ position: 'absolute', inset: 0, zIndex: 0, overflow: 'hidden' }}>
      {!supportsWebGL ? (
        <ViewerFallback />
      ) : contentMode === 'map' && geolocation.latitude !== null && geolocation.longitude !== null ? (
        <MapRenderTarget
          viewerEngine={viewerEngine}
          layerId={GIS_CURRENT_LOCATION_LAYER_ID}
          center={{ latitude: geolocation.latitude, longitude: geolocation.longitude }}
          zoom={DEFAULT_MAP_ZOOM}
          onError={revertToPlaceholder}
        />
      ) : (
        <PlaceholderRenderTarget />
      )}
      <FloatingPanelHost />
    </Box>
  )
}
