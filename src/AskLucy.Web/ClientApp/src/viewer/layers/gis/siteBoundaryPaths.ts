import { isCounterClockwise } from '../../siteBoundaryEdit/ringGeometry'

type Point = { latitude: number; longitude: number }

/** A void wound the opposite way round from the ring it is in, as Google needs for an inner path to be drawn as a hole. */
function woundAgainst(outer: readonly Point[], hole: readonly Point[]): Point[] {
  return isCounterClockwise(hole) === isCounterClockwise(outer) ? [...hole].reverse() : [...hole]
}

/**
 * specs/081: the paths of the native outline polygon. The site's rings come first, as separate outer shapes
 * (they never overlap), then every void as an inner path, which Google draws as a hole in the ring it lies in.
 * `voids[i]` belong to ring `i`, the exterior ring being ring 0.
 */
export function siteBoundaryPaths(
  exteriorRing: readonly Point[],
  additionalRings: readonly (readonly Point[])[] = [],
  voids: readonly (readonly (readonly Point[])[])[] = [],
): google.maps.LatLngLiteral[][] {
  const rings = [exteriorRing, ...additionalRings]
  const holes = rings.flatMap((ring, i) => (voids[i] ?? []).map((v) => woundAgainst(ring, v)))
  return [...rings, ...holes].map((ring) => ring.map((p) => ({ lat: p.latitude, lng: p.longitude })))
}

/** Every ring that gets an animated border: the site's rings, then each void, so a void reads as an edge too. */
export function siteBoundaryBorderRings(
  exteriorRing: readonly Point[],
  additionalRings: readonly (readonly Point[])[] = [],
  voids: readonly (readonly (readonly Point[])[])[] = [],
): Point[][] {
  return [exteriorRing, ...additionalRings, ...voids.flat()].map((ring) => [...ring])
}
