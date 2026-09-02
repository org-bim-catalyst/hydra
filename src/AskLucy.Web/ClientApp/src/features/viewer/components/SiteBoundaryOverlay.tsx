import { useEffect } from 'react'
import { useActiveSiteBoundaryStore } from '../../../store/activeSiteBoundaryStore'
import { useGoogleMapsStore } from '../../../viewer/store/googleMapsStore'

/** specs/042-site-boundary-resolution: renders the currently active site boundary (if any) as an
 * animated highlight in the map's Three.js scene, via `GoogleMapsGisLayerHandle.setSiteBoundary`.
 * Follows `POIMarkerOverlay.tsx`'s exact idiom — no DOM output, purely imperative, replaces the
 * previous boundary on every change (edge case: a new, unrelated site must not leave the old one
 * overlaid). */
export function SiteBoundaryOverlay() {
  const handle = useGoogleMapsStore((s) => s.handle)
  const polygon = useActiveSiteBoundaryStore((s) => s.polygon)
  const confidenceLevel = useActiveSiteBoundaryStore((s) => s.confidenceLevel)

  useEffect(() => {
    if (!handle) return

    if (!polygon || !confidenceLevel) {
      handle.setSiteBoundary(null)
      return
    }

    handle.setSiteBoundary({ exteriorRing: polygon, confidenceLevel })

    return () => {
      handle.setSiteBoundary(null)
    }
  }, [handle, polygon, confidenceLevel])

  return null
}
