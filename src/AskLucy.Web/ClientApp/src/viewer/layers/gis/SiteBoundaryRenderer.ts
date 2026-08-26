import * as THREE from 'three'
import {
  createAnimatedBorderHighlight,
  type AnimatedBorderHighlight,
  type BorderConfidenceLevel,
  type LocalPoint,
} from '../../effects/AnimatedBorderHighlight'

export interface SiteBoundaryRenderer {
  /** Add this once to the layer's scene — contents are swapped internally as the boundary changes. */
  object3D: THREE.Object3D
  /** Replaces the rendered boundary. Pass `null` to clear it (edge case: a new, unrelated site was referenced). */
  setPolygon(ring: LocalPoint[] | null, confidenceLevel: BorderConfidenceLevel): void
  /** Call once per frame (from the owning layer's `onDraw`) to advance the comet animation. */
  update(deltaSeconds: number): void
  dispose(): void
}

/**
 * specs/042-site-boundary-resolution — owns the `AnimatedBorderHighlight` instance for the
 * currently active site boundary. `ring` is expected already projected into local scene-space
 * meters relative to the owning `GoogleMapsGisLayer`'s fixed camera reference point
 * (`options.center`) — the same reference point `onDraw` already uses for the camera's
 * `transformer.fromLatLngAltitude` call every frame, so geometry placed here tracks correctly
 * with the live Google Maps camera as the user pans/zooms/rotates, with no separate per-object
 * transform needed (research.md #8's corrected approach — a second `transformer` call per
 * object was considered but not used, since Google's own documented Three.js sample places
 * scene content via a shared local-meters projection from one fixed anchor, not one transform
 * call per object).
 */
export function createSiteBoundaryRenderer(): SiteBoundaryRenderer {
  const group = new THREE.Group()
  let highlight: AnimatedBorderHighlight | null = null

  return {
    object3D: group,
    setPolygon(ring, confidenceLevel) {
      if (highlight) {
        group.remove(highlight.object3D)
        highlight.dispose()
        highlight = null
      }

      if (!ring || ring.length < 3) {
        return
      }

      // Ensure a closed ring (first point repeats as last) — callers may pass either form.
      const first = ring[0]
      const last = ring[ring.length - 1]
      const closed = first.x === last.x && first.y === last.y ? ring : [...ring, first]

      highlight = createAnimatedBorderHighlight(closed, confidenceLevel)
      group.add(highlight.object3D)
    },
    update(deltaSeconds) {
      highlight?.update(deltaSeconds)
    },
    dispose() {
      if (highlight) {
        group.remove(highlight.object3D)
        highlight.dispose()
        highlight = null
      }
    },
  }
}
