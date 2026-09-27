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
  /**
   * Replaces the rendered boundary. The first ring is the site; any further rings (specs/077) are
   * its separate buildings — Muscat Grand Mall's Phase 2 across the road — and each gets its own
   * animated border. Pass `null` or no rings to clear it (edge case: a new, unrelated site was
   * referenced).
   */
  setRings(rings: LocalPoint[][] | null, confidenceLevel: BorderConfidenceLevel): void
  /** Call once per frame (from the owning layer's `onDraw`) to advance the border animation.
   * `metersPerPixel` (see `AnimatedBorderHighlight.update`) keeps the border ring's on-screen
   * width constant across zoom levels. */
  update(deltaSeconds: number, metersPerPixel?: number): void
  dispose(): void
}

/**
 * specs/042-site-boundary-resolution — owns the `AnimatedBorderHighlight` instances for the
 * currently active site boundary. Rings are expected already projected into local scene-space
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
  let highlights: AnimatedBorderHighlight[] = []

  const clear = () => {
    for (const highlight of highlights) {
      group.remove(highlight.object3D)
      highlight.dispose()
    }
    highlights = []
  }

  return {
    object3D: group,
    setRings(rings, confidenceLevel) {
      clear()

      for (const ring of rings ?? []) {
        if (ring.length < 3) {
          continue
        }

        // Ensure a closed ring (first point repeats as last) — callers may pass either form.
        const first = ring[0]
        const last = ring[ring.length - 1]
        const closed = first.x === last.x && first.y === last.y ? ring : [...ring, first]

        const highlight = createAnimatedBorderHighlight(closed, confidenceLevel)
        group.add(highlight.object3D)
        highlights.push(highlight)
      }
    },
    update(deltaSeconds, metersPerPixel) {
      for (const highlight of highlights) {
        highlight.update(deltaSeconds, metersPerPixel)
      }
    },
    dispose: clear,
  }
}
