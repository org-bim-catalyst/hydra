import type { GeoPoint } from '../../store/activeSiteBoundaryStore'

/**
 * specs/079 research D1: the checks an outline edit must pass before it is applied, run locally on
 * every change so a refused drag snaps back at once. Plain TypeScript — no geometry library. The
 * server re-checks everything (NetTopologySuite); these mirror its thresholds so the two agree.
 *
 * Rings here are OPEN (no repeated closing corner), which is what `google.maps.Polygon` paths are.
 * Use {@link openRing} / {@link closeRing} at the boundary with the closed rings the API carries.
 */

const METERS_PER_DEGREE_LATITUDE = 111_320
const MINIMUM_AREA_SQUARE_METERS = 1
const DUPLICATE_CORNER_METERS = 0.05
const CROSSING_EPSILON = 1e-9

export type RefusalReason = 'tooFewCorners' | 'selfCrossing' | 'tooSmall' | 'duplicateCorner'

export interface Refusal {
  reason: RefusalReason
  /** Plain words for the user; also announced by the live region. */
  message: string
}

const REFUSALS: Record<RefusalReason, Refusal> = {
  tooFewCorners: { reason: 'tooFewCorners', message: 'An outline needs at least 3 corners.' },
  selfCrossing: { reason: 'selfCrossing', message: 'That would make the outline cross itself.' },
  tooSmall: { reason: 'tooSmall', message: 'That would make the outline too small.' },
  duplicateCorner: { reason: 'duplicateCorner', message: "Two corners can't be in the same spot." },
}

interface Point {
  x: number
  y: number
}

/** East/north metres around `reference` — the same equirectangular frame the server uses. */
function project(ring: readonly GeoPoint[], reference: GeoPoint): Point[] {
  const metersPerDegreeLongitude = METERS_PER_DEGREE_LATITUDE * Math.cos((reference.latitude * Math.PI) / 180)
  return ring.map((p) => ({
    x: (p.longitude - reference.longitude) * metersPerDegreeLongitude,
    y: (p.latitude - reference.latitude) * METERS_PER_DEGREE_LATITUDE,
  }))
}

/** Drops a repeated closing corner, if present. */
export function openRing(ring: readonly GeoPoint[]): GeoPoint[] {
  const last = ring.length - 1
  return last > 0 && ring[0].latitude === ring[last].latitude && ring[0].longitude === ring[last].longitude
    ? ring.slice(0, last)
    : [...ring]
}

/** Repeats the first corner at the end, as the API's closed rings do. */
export function closeRing(ring: readonly GeoPoint[]): GeoPoint[] {
  return ring.length === 0 ? [] : [...ring, ring[0]]
}

function signedArea(points: readonly Point[]): number {
  let sum = 0
  for (let i = 0; i < points.length; i++) {
    const a = points[i]
    const b = points[(i + 1) % points.length]
    sum += a.x * b.y - b.x * a.y
  }
  return sum / 2
}

/** Area of a ring in square metres (shoelace, local metric frame); 0 for fewer than 3 corners. */
export function ringAreaSquareMeters(ring: readonly GeoPoint[]): number {
  const open = openRing(ring)
  if (open.length < 3) return 0
  return Math.abs(signedArea(project(open, open[0])))
}

function orientation(a: Point, b: Point, c: Point): number {
  const value = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)
  return Math.abs(value) < CROSSING_EPSILON ? 0 : value > 0 ? 1 : -1
}

function onSegment(a: Point, b: Point, p: Point): boolean {
  return (
    Math.min(a.x, b.x) - CROSSING_EPSILON <= p.x &&
    p.x <= Math.max(a.x, b.x) + CROSSING_EPSILON &&
    Math.min(a.y, b.y) - CROSSING_EPSILON <= p.y &&
    p.y <= Math.max(a.y, b.y) + CROSSING_EPSILON
  )
}

/** True when two segments cross or touch anywhere, including collinear overlap. */
function segmentsIntersect(a: Point, b: Point, c: Point, d: Point): boolean {
  const o1 = orientation(a, b, c)
  const o2 = orientation(a, b, d)
  const o3 = orientation(c, d, a)
  const o4 = orientation(c, d, b)
  if (o1 !== o2 && o3 !== o4) return true
  if (o1 === 0 && onSegment(a, b, c)) return true
  if (o2 === 0 && onSegment(a, b, d)) return true
  if (o3 === 0 && onSegment(c, d, a)) return true
  if (o4 === 0 && onSegment(c, d, b)) return true
  return false
}

function distance(a: Point, b: Point): number {
  return Math.hypot(a.x - b.x, a.y - b.y)
}

function hasDuplicateNear(points: readonly Point[], index: number): boolean {
  return points.some((p, i) => i !== index && distance(p, points[index]) < DUPLICATE_CORNER_METERS)
}

/**
 * Does an edge touching corner `index` cross any edge it isn't next to? O(n): only the two edges
 * incident to `index` are tested, against every other edge. Edges that share an endpoint with the
 * tested edge are skipped — they legitimately touch there.
 */
function crossesAtCorner(points: readonly Point[], index: number): boolean {
  const n = points.length
  const incident = [
    [(index - 1 + n) % n, index],
    [index, (index + 1) % n],
  ]
  for (const [from, to] of incident) {
    for (let e = 0; e < n; e++) {
      const eNext = (e + 1) % n
      if (e === from || eNext === from || e === to || eNext === to) continue
      if (segmentsIntersect(points[from], points[to], points[e], points[eNext])) return true
    }
  }
  return false
}

/**
 * Checks the change made at corner `index` of `ring` (a moved corner, an inserted one, or — for a
 * deletion — the corner before the one removed, which is where the new edge starts). Returns the
 * refusal, or null when the ring is still acceptable. O(n).
 */
export function validateChange(ring: readonly GeoPoint[], index: number): Refusal | null {
  const open = openRing(ring)
  if (open.length < 3) return REFUSALS.tooFewCorners
  const points = project(open, open[0])
  const at = ((index % points.length) + points.length) % points.length

  if (hasDuplicateNear(points, at)) return REFUSALS.duplicateCorner
  if (crossesAtCorner(points, at)) return REFUSALS.selfCrossing
  if (Math.abs(signedArea(points)) < MINIMUM_AREA_SQUARE_METERS) return REFUSALS.tooSmall
  return null
}

/** Metres east/north of `reference` for each corner - the frame the shape tools work in. */
export function toLocalMeters(ring: readonly GeoPoint[], reference: GeoPoint): { x: number; y: number }[] {
  return project(ring, reference)
}

/** The inverse of {@link toLocalMeters}. */
export function fromLocalMeters(points: readonly { x: number; y: number }[], reference: GeoPoint): GeoPoint[] {
  const metersPerDegreeLongitude = METERS_PER_DEGREE_LATITUDE * Math.cos((reference.latitude * Math.PI) / 180)
  return points.map((p) => ({
    latitude: reference.latitude + p.y / METERS_PER_DEGREE_LATITUDE,
    longitude: reference.longitude + p.x / metersPerDegreeLongitude,
  }))
}

/** Whether the ring runs counter-clockwise (east/north frame), which decides which side of an edge is "outward". */
export function isCounterClockwise(ring: readonly GeoPoint[]): boolean {
  const open = openRing(ring)
  return open.length >= 3 && signedArea(project(open, open[0])) > 0
}

/** A ring with more corners than this is simplified when editing starts: too many tiny handles to work with. */
export const DENSE_RING_CORNERS = 120

/** How far, in metres, the simplified outline may stray from the original at any point. */
export const SIMPLIFY_TOLERANCE_METERS = 0.5

export interface SimplifiedRing {
  /** Open ring. The original, untouched, when nothing was removed. */
  ring: GeoPoint[]
  removed: number
}

/** Distance in metres from `p` to the segment `a`-`b`. */
function distanceToSegment(p: Point, a: Point, b: Point): number {
  const dx = b.x - a.x
  const dy = b.y - a.y
  const lengthSquared = dx * dx + dy * dy
  if (lengthSquared === 0) return distance(p, a)
  const t = Math.max(0, Math.min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / lengthSquared))
  return distance(p, { x: a.x + t * dx, y: a.y + t * dy })
}

/** Ramer-Douglas-Peucker over `points[from..to]` (inclusive), returning the indices kept. Iterative, so a 2,000-corner ring cannot overflow the stack. */
function keptIndices(points: readonly Point[], from: number, to: number, tolerance: number): Set<number> {
  const kept = new Set<number>([from, to])
  const stack: [number, number][] = [[from, to]]

  while (stack.length > 0) {
    const [start, end] = stack.pop()!
    let farthest = -1
    let farthestDistance = tolerance
    for (let i = start + 1; i < end; i++) {
      const d = distanceToSegment(points[i], points[start], points[end])
      if (d > farthestDistance) {
        farthest = i
        farthestDistance = d
      }
    }
    if (farthest !== -1) {
      kept.add(farthest)
      stack.push([start, farthest], [farthest, end])
    }
  }

  return kept
}

/**
 * Drops corners that do not change the shape by more than `toleranceMeters` (Ramer-Douglas-Peucker on
 * the closed ring, split at corner 0 and the corner farthest from it so no corner is privileged). The
 * result must still be a valid outline - simple, not collapsed - or the tolerance is halved and it is
 * tried again, and the original is returned if no attempt is valid. Never returns fewer than 3
 * corners, and a ring already at or under `DENSE_RING_CORNERS` is left exactly as it is.
 */
export function simplifyRing(ring: readonly GeoPoint[], toleranceMeters = SIMPLIFY_TOLERANCE_METERS): SimplifiedRing {
  const open = openRing(ring)
  if (open.length <= DENSE_RING_CORNERS) return { ring: open, removed: 0 }

  const points = project(open, open[0])

  let far = 0
  for (let i = 1; i < points.length; i++) {
    if (distance(points[i], points[0]) > distance(points[far], points[0])) far = i
  }

  let tolerance = toleranceMeters
  for (let attempt = 0; attempt < 4; attempt++) {
    const first = keptIndices(points, 0, far, tolerance)
    // The second half wraps from `far` back round to corner 0, so it is indexed on a ring closed by repeating corner 0.
    const wrapped = [...points, points[0]]
    const second = keptIndices(wrapped, far, points.length, tolerance)
    const kept = [...new Set([...first, ...[...second].map((i) => i % points.length)])].sort((a, b) => a - b)

    if (kept.length >= 3 && kept.length < open.length) {
      const candidate = kept.map((i) => open[i])
      if (validateRing(candidate) === null) return { ring: candidate, removed: open.length - kept.length }
    }
    tolerance /= 2
  }

  return { ring: open, removed: 0 }
}

/** Checks a whole ring from scratch. O(n²) — for entry and save, not for every drag step. */
export function validateRing(ring: readonly GeoPoint[]): Refusal | null {
  const open = openRing(ring)
  if (open.length < 3) return REFUSALS.tooFewCorners
  const points = project(open, open[0])

  for (let i = 0; i < points.length; i++) {
    if (hasDuplicateNear(points, i)) return REFUSALS.duplicateCorner
  }
  for (let i = 0; i < points.length; i++) {
    if (crossesAtCorner(points, i)) return REFUSALS.selfCrossing
  }
  if (Math.abs(signedArea(points)) < MINIMUM_AREA_SQUARE_METERS) return REFUSALS.tooSmall
  return null
}
