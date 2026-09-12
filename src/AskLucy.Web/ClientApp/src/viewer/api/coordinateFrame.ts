import { sceneAnchor } from '../scene/SceneAnchor'

/** contracts/coordinate-frame-and-drawing-space.md — the ONE local positioning convention every
 * capability draws in.
 *
 * X = metres East of the viewer's single reference point.
 * Y = metres North of the viewer's single reference point.
 * Z = metres above ground.
 *
 * Maps directly onto this scene's own Three.js axes with no remapping — a property of how
 * `WebGLOverlayView`'s `transformer.fromLatLngAltitude` camera matrix sets up the local tangent
 * plane (Google's own documented recipe), confirmed against the existing
 * `SiteBoundaryRenderer`/`AnimatedBorderHighlight` code, which already places a `{x,y}` local
 * point straight into `new THREE.Vector3(x, y, 0)`. A capability drawing through a
 * `DrawingSpaceHandle` can therefore use an `ENU`/`LocalPosition` value as a Three.js position
 * directly. */
export interface LocalPosition {
  x: number
  y: number
  z: number
}

/** research D2 — the same equirectangular projection `GoogleMapsGisLayer.ts` already used for its
 * own (now-removed) private `toLocalMeters` helper, generalized into the one published
 * conversion. Accurate to within 1 metre at the working scale this feature targets (SC-002) — the
 * same tolerance the existing approximation already delivers at typical site distances, not
 * unlimited-precision geodesy. */
const METERS_PER_DEGREE_LATITUDE = 111_320

/** Pure function of the *current* reference point (research D2's "not a stale re-anchor" claim
 * hinges on this reading `sceneAnchor.get()` fresh every call, never caching it). Throws if no
 * content has loaded yet — the reference point genuinely doesn't exist until then, and a caller
 * asking for a conversion before any content exists has no meaningful answer to receive. */
export function worldToLocal(point: { latitude: number; longitude: number }, altitudeMetres: number): LocalPosition {
  const reference = sceneAnchor.get()
  if (!reference) {
    throw new Error('worldToLocal: no reference point set — no content has loaded yet.')
  }

  const metersPerDegreeLongitude = METERS_PER_DEGREE_LATITUDE * Math.cos((reference.latitude * Math.PI) / 180)
  return {
    x: (point.longitude - reference.longitude) * metersPerDegreeLongitude,
    y: (point.latitude - reference.latitude) * METERS_PER_DEGREE_LATITUDE,
    z: altitudeMetres,
  }
}

export function localToWorld(position: LocalPosition): { latitude: number; longitude: number; altitudeMetres: number } {
  const reference = sceneAnchor.get()
  if (!reference) {
    throw new Error('localToWorld: no reference point set — no content has loaded yet.')
  }

  const metersPerDegreeLongitude = METERS_PER_DEGREE_LATITUDE * Math.cos((reference.latitude * Math.PI) / 180)
  return {
    latitude: reference.latitude + position.y / METERS_PER_DEGREE_LATITUDE,
    longitude: reference.longitude + position.x / metersPerDegreeLongitude,
    altitudeMetres: position.z,
  }
}
