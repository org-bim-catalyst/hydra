import { useEffect, useSyncExternalStore } from 'react'
import { useActiveSiteBoundaryStore } from '../../../store/activeSiteBoundaryStore'
import { sceneAnchor } from '../../../viewer/scene/SceneAnchor'
import { useGoogleMapsStore } from '../../../viewer/store/googleMapsStore'

/** specs/042-site-boundary-resolution: renders the currently active site boundary (if any) as an
 * animated highlight in the map's Three.js scene, via `GoogleMapsGisLayerHandle.setSiteBoundary`.
 * Follows `POIMarkerOverlay.tsx`'s exact idiom — no DOM output, purely imperative, replaces the
 * previous boundary on every change (edge case: a new, unrelated site must not leave the old one
 * overlaid).
 *
 * `setSiteBoundary` converts the ring into metres from the scene's reference point at the moment
 * it is called, so it is re-applied whenever that point moves (`anchorVersion`) — otherwise a ring
 * built before the anchor followed the active location would stay offset from its real position. */
export function SiteBoundaryOverlay() {
  const handle = useGoogleMapsStore((s) => s.handle)
  const polygon = useActiveSiteBoundaryStore((s) => s.polygon)
  const additionalPolygons = useActiveSiteBoundaryStore((s) => s.additionalPolygons)
  const confidenceLevel = useActiveSiteBoundaryStore((s) => s.confidenceLevel)
  const anchorVersion = useSyncExternalStore(sceneAnchor.subscribe, () => sceneAnchor.version)

  useEffect(() => {
    if (!handle) return

    if (!polygon || !confidenceLevel) {
      handle.setSiteBoundary(null)
      return
    }

    handle.setSiteBoundary({ exteriorRing: polygon, additionalRings: additionalPolygons, confidenceLevel })

    return () => {
      handle.setSiteBoundary(null)
    }
  }, [handle, polygon, additionalPolygons, confidenceLevel, anchorVersion])

  return null
}
