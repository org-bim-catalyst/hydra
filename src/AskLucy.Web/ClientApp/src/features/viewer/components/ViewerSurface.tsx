import { Box, Chip } from '@mui/material'
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
import { useActiveLocationStore } from '../../../store/activeLocationStore'

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

/** FR-001: the viewer's full-viewport mount point and primary workspace surface. Reads the
 * active location from `activeLocationStore` (specs/036-startup-geolocation) — no longer a
 * prop — so both startup geolocation and agent-confirmed locations (specs/035, spec 036 US3)
 * drive the viewer through the same shared store. */
/** spec.md Edge Cases: shared by "location became unavailable" (FR-012) and "the map/GIS
 * provider is unreachable" — both revert to the placeholder the same way. */
function revertToPlaceholder() {
  viewerEngine.removeLayer(GIS_CURRENT_LOCATION_LAYER_ID)
  useViewerEngineStore.getState().setContentMode('placeholder')
}

export function ViewerSurface() {
  const supportsWebGL = useWebGLSupport()
  const contentMode = useViewerEngineStore((s) => s.contentMode)
  const { isLive: isPanelHubLive } = useFloatingPanelHub()

  // specs/036-startup-geolocation: read from shared active location store.
  // source !== null means a location is set (either from device or agent); null means no location.
  const source = useActiveLocationStore((s) => s.source)
  const latitude = useActiveLocationStore((s) => s.latitude)
  const longitude = useActiveLocationStore((s) => s.longitude)

  useEffect(() => {
    const store = useViewerEngineStore.getState()

    if (source !== null && latitude !== null && longitude !== null) {
      const center = { latitude, longitude }
      // FR-007: replaces the placeholder as the active view once a location is set. Only added
      // once — a coordinate update (user physically moved, or agent confirmed a new location)
      // just re-centers via zoomToLocation below, it doesn't re-add the layer.
      if (store.contentMode !== 'map') {
        viewerEngine.addLayer({
          id: GIS_CURRENT_LOCATION_LAYER_ID,
          kind: 'gis',
          metadata: { provider: 'google-maps', center, zoom: DEFAULT_MAP_ZOOM },
        })
        useViewerEngineStore.getState().setContentMode('map')
      }
      viewerEngine.zoomToLocation(center.latitude, center.longitude, DEFAULT_MAP_ZOOM)
    } else if (source === null && store.contentMode === 'map') {
      // FR-012: location became unavailable after the map was already active (e.g. permission
      // revoked mid-session) — revert to the placeholder. When no location was ever set,
      // contentMode is already 'placeholder' and this branch is never reached.
      revertToPlaceholder()
    }
  }, [source, latitude, longitude])

  return (
    <Box sx={{ position: 'absolute', inset: 0, zIndex: 0, overflow: 'hidden' }}>
      {!supportsWebGL ? (
        <ViewerFallback />
      ) : contentMode === 'map' && latitude !== null && longitude !== null ? (
        <MapRenderTarget
          viewerEngine={viewerEngine}
          layerId={GIS_CURRENT_LOCATION_LAYER_ID}
          center={{ latitude, longitude }}
          zoom={DEFAULT_MAP_ZOOM}
          onError={revertToPlaceholder}
        />
      ) : (
        <PlaceholderRenderTarget />
      )}
      <FloatingPanelHost />
      {/* specs/029-fix-chat-widget-bugs FR-010/analysis finding C1 — same Chip treatment
          ExecutionMonitor already uses for useWorkflowExecutionHub's isLive, adapted to only
          mount while reconnecting: this is an ambient full-viewport surface, not a monitoring
          dashboard, so a permanent "Live" badge for a niche feature (AI-requested panels)
          would be visual noise most users never need to see. */}
      {!isPanelHubLive && (
        <Chip
          label="Reconnecting…"
          size="small"
          variant="outlined"
          color="default"
          data-testid="panel-hub-connection-status"
          sx={{ position: 'absolute', top: 12, right: 12, bgcolor: 'background.paper' }}
        />
      )}
    </Box>
  )
}
