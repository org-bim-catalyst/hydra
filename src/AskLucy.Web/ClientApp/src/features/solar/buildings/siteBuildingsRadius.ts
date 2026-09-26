import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'

/** The server's own default query radius (`Buildings:Overpass:DefaultRadiusMetres`). */
export const DEFAULT_SITE_BUILDINGS_RADIUS_METRES = 200

/** Neighbours beyond the site's edge that still cast onto it. */
const MARGIN_METRES = 100
const STEP_METRES = 50
const MAX_RADIUS_METRES = 500

const METRES_PER_DEGREE = 111_320

/**
 * specs/076 — how far around the site point to ask for buildings. A large site (BurJuman's two
 * merged mall ways reach well past 200 m from the site point) needs its whole outline plus a margin of
 * neighbours; the fixed 200 m left masses missing across half the dome. Rounded up to 50 m so small
 * differences between boundaries share one cached server answer, and capped at 500 m, where the
 * server still answers in about 9 s with roughly 400 buildings.
 */
export function siteBuildingsRadiusFor(site: GeoPoint, boundary: GeoPoint[] | null): number {
  if (!boundary || boundary.length < 3) return DEFAULT_SITE_BUILDINGS_RADIUS_METRES

  // Equirectangular distance: accurate to well under a metre at these ranges.
  const metresPerLongitude = METRES_PER_DEGREE * Math.cos((site.latitude * Math.PI) / 180)
  const furthest = Math.max(
    ...boundary.map((point) =>
      Math.hypot((point.latitude - site.latitude) * METRES_PER_DEGREE, (point.longitude - site.longitude) * metresPerLongitude),
    ),
  )
  const radius = Math.ceil((furthest + MARGIN_METRES) / STEP_METRES) * STEP_METRES
  return Math.min(MAX_RADIUS_METRES, Math.max(DEFAULT_SITE_BUILDINGS_RADIUS_METRES, radius))
}
