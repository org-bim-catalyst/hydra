import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import { fromLocalMeters, isCounterClockwise, openRing, toLocalMeters } from './ringGeometry'

/**
 * specs/079: the shape tools - round a corner, curve an edge, make a ring a circle. Each returns the
 * ring's new corners, or a refusal in plain words. They add corners along a curve; the caller checks
 * the whole result (a valid, simple outline) before applying it, so a tool never has to.
 *
 * All work is in a local metre frame around the ring, then converted back, so a radius is really metres.
 */

export type ShapeResult = { ring: GeoPoint[] } | { refusal: string }

/** One corner every this many degrees along a curve: smooth to the eye, few enough to edit. */
const ARC_STEP_DEGREES = 6
const RADIANS = Math.PI / 180

/** A corner within this many degrees of a straight line is not rounded. */
const STRAIGHT_TOLERANCE_DEGREES = 1

interface Vec {
  x: number
  y: number
}

const sub = (a: Vec, b: Vec): Vec => ({ x: a.x - b.x, y: a.y - b.y })
const add = (a: Vec, b: Vec): Vec => ({ x: a.x + b.x, y: a.y + b.y })
const scale = (a: Vec, k: number): Vec => ({ x: a.x * k, y: a.y * k })
const length = (a: Vec): number => Math.hypot(a.x, a.y)
const unit = (a: Vec): Vec => scale(a, 1 / length(a))
const dot = (a: Vec, b: Vec): number => a.x * b.x + a.y * b.y

/** Points along a circle around `centre` from angle `from` sweeping `sweep` radians, both ends included. */
function arcPoints(centre: Vec, radius: number, from: number, sweep: number): Vec[] {
  const steps = Math.max(2, Math.ceil(Math.abs(sweep) / (ARC_STEP_DEGREES * RADIANS)))
  return Array.from({ length: steps + 1 }, (_, i) => {
    const angle = from + (sweep * i) / steps
    return { x: centre.x + radius * Math.cos(angle), y: centre.y + radius * Math.sin(angle) }
  })
}

/** Angle difference wrapped into (-pi, pi]. */
const wrapAngle = (angle: number): number => {
  let a = angle
  while (a > Math.PI) a -= 2 * Math.PI
  while (a <= -Math.PI) a += 2 * Math.PI
  return a
}

/**
 * Replaces corner `index` with an arc of the given radius (metres) that meets both edges smoothly.
 * The curve starts and ends part-way along the two edges, so the edges must be long enough: the
 * arc may use at most half of each. When the radius is too big the refusal says the largest that fits.
 */
export function roundCorner(ring: readonly GeoPoint[], index: number, radiusMeters: number): ShapeResult {
  const open = openRing(ring)
  if (!(radiusMeters > 0)) return { refusal: 'Enter a radius greater than zero.' }
  if (index < 0 || index >= open.length) return { refusal: 'Select a corner first.' }

  const points = toLocalMeters(open, open[0])
  const n = points.length
  const corner = points[index]
  const towardPrev = sub(points[(index - 1 + n) % n], corner)
  const towardNext = sub(points[(index + 1) % n], corner)
  const lengthPrev = length(towardPrev)
  const lengthNext = length(towardNext)
  if (lengthPrev === 0 || lengthNext === 0) return { refusal: 'That corner sits on top of its neighbour.' }

  const u = unit(towardPrev)
  const w = unit(towardNext)
  const angle = Math.acos(Math.max(-1, Math.min(1, dot(u, w))))
  if (angle < STRAIGHT_TOLERANCE_DEGREES * RADIANS || angle > Math.PI - STRAIGHT_TOLERANCE_DEGREES * RADIANS) {
    return { refusal: 'That corner is already straight - there is nothing to round.' }
  }

  const tangentDistance = radiusMeters / Math.tan(angle / 2)
  const room = Math.min(lengthPrev, lengthNext) / 2
  if (tangentDistance > room) {
    const largest = Math.floor(room * Math.tan(angle / 2) * 10 + 1e-6) / 10
    return { refusal: `That radius is too big for this corner. The most that fits is ${largest} m.` }
  }

  const start = add(corner, scale(u, tangentDistance))
  const end = add(corner, scale(w, tangentDistance))
  const centre = add(corner, scale(unit(add(u, w)), radiusMeters / Math.sin(angle / 2)))
  const from = Math.atan2(start.y - centre.y, start.x - centre.x)
  const sweep = wrapAngle(Math.atan2(end.y - centre.y, end.x - centre.x) - from)

  const curve = arcPoints(centre, radiusMeters, from, sweep)
  const before = points.slice(0, index)
  const after = points.slice(index + 1)
  return { ring: fromLocalMeters([...before, ...curve, ...after], open[0]) }
}

/**
 * Bends the edge from corner `index` to the next one into an arc that bulges `bulgeMeters` at its
 * middle (the sagitta): positive bulges outward, negative inward. The most a single arc can bulge is
 * half the edge (a semicircle).
 */
export function curveEdge(ring: readonly GeoPoint[], index: number, bulgeMeters: number): ShapeResult {
  const open = openRing(ring)
  if (!Number.isFinite(bulgeMeters) || bulgeMeters === 0) return { refusal: 'Enter how far the edge should bulge, in metres.' }
  if (index < 0 || index >= open.length) return { refusal: 'Select a corner first.' }

  const points = toLocalMeters(open, open[0])
  const n = points.length
  const from = points[index]
  const to = points[(index + 1) % n]
  const chord = length(sub(to, from))
  if (chord === 0) return { refusal: 'That edge has no length.' }

  // A whisker of tolerance so an exact semicircle is not refused over floating-point rounding.
  const sagitta = Math.min(Math.abs(bulgeMeters), chord / 2)
  if (Math.abs(bulgeMeters) > chord / 2 + 1e-6) {
    const most = Math.floor((chord / 2) * 10 + 1e-6) / 10
    return { refusal: `That bulge is too big for an edge ${Math.round(chord * 10) / 10} m long. The most is ${most} m.` }
  }

  const along = unit(sub(to, from))
  // The ring's outward side of this edge: the right of travel for a counter-clockwise ring, the left otherwise.
  const right: Vec = { x: along.y, y: -along.x }
  const outward = isCounterClockwise(open) ? right : scale(right, -1)
  const bulgeSide = bulgeMeters > 0 ? outward : scale(outward, -1)

  const radius = (chord * chord) / (8 * sagitta) + sagitta / 2
  const middle = scale(add(from, to), 0.5)
  const centre = sub(middle, scale(bulgeSide, radius - sagitta))
  const startAngle = Math.atan2(from.y - centre.y, from.x - centre.x)
  const sweepMagnitude = 2 * Math.asin(Math.min(1, chord / (2 * radius)))

  // Sweep whichever way carries the arc through the bulged middle rather than the far side.
  const towardBulge = (curve: Vec[]) => dot(sub(curve[Math.floor(curve.length / 2)], middle), bulgeSide)
  const one = arcPoints(centre, radius, startAngle, sweepMagnitude)
  const other = arcPoints(centre, radius, startAngle, -sweepMagnitude)
  const curve = towardBulge(one) >= towardBulge(other) ? one : other

  // Only the corners strictly between the ends of the edge are new; the ends are the existing corners.
  const inner = curve.slice(1, -1)
  const result = [...points.slice(0, index + 1), ...inner, ...points.slice(index + 1)]
  return { ring: fromLocalMeters(result, open[0]) }
}

/** Corners in a circle of the given radius (metres) around `centre`, counter-clockwise. */
export function circleRing(centre: GeoPoint, radiusMeters: number, corners = 72): ShapeResult {
  if (!(radiusMeters > 0)) return { refusal: 'Enter a radius greater than zero.' }
  const points = Array.from({ length: corners }, (_, i) => {
    const angle = (2 * Math.PI * i) / corners
    return { x: radiusMeters * Math.cos(angle), y: radiusMeters * Math.sin(angle) }
  })
  return { ring: fromLocalMeters(points, centre) }
}

/** The average of a ring's corners: a good enough centre for a circle to replace it. */
export function ringCentre(ring: readonly GeoPoint[]): GeoPoint {
  const open = openRing(ring)
  return {
    latitude: open.reduce((sum, p) => sum + p.latitude, 0) / open.length,
    longitude: open.reduce((sum, p) => sum + p.longitude, 0) / open.length,
  }
}

/** The radius of the circle with the same area as `areaSquareMeters` - a sensible starting value for {@link circleRing}. */
export const equivalentRadius = (areaSquareMeters: number): number => Math.sqrt(areaSquareMeters / Math.PI)

export interface PixelBox {
  left: number
  top: number
  right: number
  bottom: number
}

/** Indices of the corners whose on-screen position falls inside the box; corners the map cannot place are skipped. */
export function cornersInBox(
  corners: readonly GeoPoint[],
  toPixel: (point: GeoPoint) => { x: number; y: number } | null,
  box: PixelBox,
): number[] {
  const left = Math.min(box.left, box.right)
  const right = Math.max(box.left, box.right)
  const top = Math.min(box.top, box.bottom)
  const bottom = Math.max(box.top, box.bottom)

  const inside: number[] = []
  corners.forEach((corner, index) => {
    const pixel = toPixel(corner)
    if (pixel && pixel.x >= left && pixel.x <= right && pixel.y >= top && pixel.y <= bottom) inside.push(index)
  })
  return inside
}
