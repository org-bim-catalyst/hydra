import { describe, expect, it } from 'vitest'
import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import {
  DENSE_RING_CORNERS,
  SIMPLIFY_TOLERANCE_METERS,
  closeRing,
  openRing,
  ringAreaSquareMeters,
  simplifyDenseRing,
  simplifyRing,
  validateChange,
  validateRing,
} from './ringGeometry'

// Muscat. Same frame as the backend's NtsSiteRingGeometryTests, so the two agree.
const LAT = 23.59
const LON = 58.4
const METERS_PER_DEGREE_LATITUDE = 111_320
const metersPerDegreeLongitude = METERS_PER_DEGREE_LATITUDE * Math.cos((LAT * Math.PI) / 180)

const point = (east: number, north: number): GeoPoint => ({
  latitude: LAT + north / METERS_PER_DEGREE_LATITUDE,
  longitude: LON + east / metersPerDegreeLongitude,
})

const rectangle = (east: number, north: number, width: number, height: number): GeoPoint[] => [
  point(east, north),
  point(east + width, north),
  point(east + width, north + height),
  point(east, north + height),
]

describe('ringAreaSquareMeters', () => {
  it('measures a 100 x 100 m square as 10,000 m2', () => {
    expect(ringAreaSquareMeters(rectangle(0, 0, 100, 100))).toBeCloseTo(10_000, -1)
  })

  it('ignores a repeated closing corner', () => {
    expect(ringAreaSquareMeters(closeRing(rectangle(0, 0, 100, 100)))).toBeCloseTo(10_000, -1)
  })

  it('is 0 for fewer than 3 corners', () => {
    expect(ringAreaSquareMeters([point(0, 0), point(10, 10)])).toBe(0)
  })
})

describe('openRing / closeRing', () => {
  it('round-trips a ring', () => {
    const ring = rectangle(0, 0, 10, 10)
    expect(openRing(closeRing(ring))).toEqual(ring)
  })

  it('leaves an already-open ring alone', () => {
    expect(openRing(rectangle(0, 0, 10, 10))).toHaveLength(4)
  })
})

describe('validateRing', () => {
  it('accepts a simple ring', () => {
    expect(validateRing(rectangle(0, 0, 100, 100))).toBeNull()
  })

  it('refuses a bow-tie as crossing itself', () => {
    const bowTie = [point(0, 0), point(100, 100), point(100, 0), point(0, 100)]
    expect(validateRing(bowTie)?.reason).toBe('selfCrossing')
  })

  it('refuses fewer than 3 corners', () => {
    expect(validateRing([point(0, 0), point(10, 10)])?.reason).toBe('tooFewCorners')
  })

  it('refuses a ring under one square metre', () => {
    expect(validateRing(rectangle(0, 0, 0.5, 0.5))?.reason).toBe('tooSmall')
  })

  it('refuses two corners under 5 cm apart', () => {
    const ring = [point(0, 0), point(100, 0), point(100, 100), point(100.02, 100), point(0, 100)]
    expect(validateRing(ring)?.reason).toBe('duplicateCorner')
  })

  it('carries the user-facing message', () => {
    expect(validateRing([point(0, 0), point(1, 1)])?.message).toBe('An outline needs at least 3 corners.')
  })
})

describe('validateChange', () => {
  it('accepts moving a corner within the shape', () => {
    const ring = rectangle(0, 0, 100, 100)
    ring[2] = point(120, 110)
    expect(validateChange(ring, 2)).toBeNull()
  })

  it('refuses dragging a corner across the opposite edge', () => {
    const ring = rectangle(0, 0, 100, 100)
    // Corner 2 (top-right) dragged below the bottom edge: the edge to corner 3 now cuts across it.
    ring[2] = point(50, -50)
    expect(validateChange(ring, 2)?.reason).toBe('selfCrossing')
  })

  it('refuses moving a corner onto another corner', () => {
    const ring = rectangle(0, 0, 100, 100)
    ring[1] = point(0.01, 0)
    expect(validateChange(ring, 1)?.reason).toBe('duplicateCorner')
  })

  it('accepts inserting a corner on an edge, pushed outward', () => {
    const ring = rectangle(0, 0, 100, 100)
    ring.splice(1, 0, point(50, -20))
    expect(validateChange(ring, 1)).toBeNull()
  })

  it('accepts a deletion that keeps the ring simple, checked at the corner before the removed one', () => {
    const ring = [...rectangle(0, 0, 100, 100), point(50, 130)].filter((_, i) => i !== 4)
    // Removing the peak leaves the square; the new edge starts at corner 3.
    expect(validateChange(ring, 3)).toBeNull()
  })

  it('refuses a deletion that would leave fewer than 3 corners', () => {
    expect(validateChange([point(0, 0), point(10, 0)], 0)?.reason).toBe('tooFewCorners')
  })

  it('agrees with a full validation on a valid drag', () => {
    const ring = rectangle(0, 0, 100, 100)
    ring[3] = point(-10, 105)
    expect(validateChange(ring, 3)).toBeNull()
    expect(validateRing(ring)).toBeNull()
  })

  it('validates one change on a 500-corner ring in well under 4 ms', () => {
    const ring: GeoPoint[] = []
    for (let i = 0; i < 500; i++) {
      const angle = (2 * Math.PI * i) / 500
      ring.push(point(200 * Math.cos(angle), 200 * Math.sin(angle)))
    }
    const start = performance.now()
    for (let i = 0; i < 20; i++) validateChange(ring, 250)
    const perChange = (performance.now() - start) / 20
    expect(validateChange(ring, 250)).toBeNull()
    expect(perChange).toBeLessThan(4)
  })
})

describe('simplifyRing (dense outlines, when editing starts)', () => {
  /** A circle of `n` corners and radius `r` metres. */
  const circle = (n: number, r: number): GeoPoint[] =>
    Array.from({ length: n }, (_, i) => point(r * Math.cos((2 * Math.PI * i) / n), r * Math.sin((2 * Math.PI * i) / n)))

  /** Distance in metres from a point to the nearest edge of a ring. */
  const toRing = (p: GeoPoint, ring: GeoPoint[]): number => {
    const mx = 111_320 * Math.cos((LAT * Math.PI) / 180)
    const at = (g: GeoPoint) => ({ x: (g.longitude - LON) * mx, y: (g.latitude - LAT) * 111_320 })
    const q = at(p)
    let best = Infinity
    for (let i = 0; i < ring.length; i++) {
      const a = at(ring[i])
      const b = at(ring[(i + 1) % ring.length])
      const dx = b.x - a.x
      const dy = b.y - a.y
      const t = dx === 0 && dy === 0 ? 0 : Math.max(0, Math.min(1, ((q.x - a.x) * dx + (q.y - a.y) * dy) / (dx * dx + dy * dy)))
      best = Math.min(best, Math.hypot(q.x - (a.x + t * dx), q.y - (a.y + t * dy)))
    }
    return best
  }

  it('leaves a ring at or under the dense threshold exactly as it is', () => {
    const ring = circle(DENSE_RING_CORNERS, 200)
    const result = simplifyRing(ring)

    expect(result.removed).toBe(0)
    expect(result.ring).toEqual(ring)
  })

  it('drops the corners that do not change the shape of a dense ring', () => {
    const dense = circle(600, 200)
    const { ring, removed } = simplifyRing(dense)

    expect(ring.length).toBeLessThan(dense.length / 3)
    expect(removed).toBe(dense.length - ring.length)
    expect(ring.length).toBeGreaterThanOrEqual(3)
  })

  it('keeps every original corner within the tolerance of the simplified outline', () => {
    const dense = circle(600, 200)
    const { ring } = simplifyRing(dense)

    const worst = Math.max(...dense.map((p) => toRing(p, ring)))
    expect(worst).toBeLessThanOrEqual(SIMPLIFY_TOLERANCE_METERS + 1e-6)
  })

  it('keeps the area within half a percent', () => {
    const dense = circle(600, 200)
    const { ring } = simplifyRing(dense)

    expect(Math.abs(ringAreaSquareMeters(ring) - ringAreaSquareMeters(dense)) / ringAreaSquareMeters(dense)).toBeLessThan(0.005)
  })

  it('returns a valid outline that only uses corners the ring already had', () => {
    const dense = circle(600, 200)
    const { ring } = simplifyRing(dense)

    expect(validateRing(ring)).toBeNull()
    expect(ring.every((p) => dense.some((d) => d.latitude === p.latitude && d.longitude === p.longitude))).toBe(true)
  })

  it('keeps the sharp corners of a dense rectangle and drops the points along its edges', () => {
    const edge = (from: GeoPoint, to: GeoPoint, steps: number) =>
      Array.from({ length: steps }, (_, i) => ({
        latitude: from.latitude + ((to.latitude - from.latitude) * i) / steps,
        longitude: from.longitude + ((to.longitude - from.longitude) * i) / steps,
      }))
    const c = [point(0, 0), point(300, 0), point(300, 100), point(0, 100)]
    const dense = [...edge(c[0], c[1], 50), ...edge(c[1], c[2], 50), ...edge(c[2], c[3], 50), ...edge(c[3], c[0], 50)]

    const { ring, removed } = simplifyRing(dense)

    expect(ring).toHaveLength(4)
    expect(removed).toBe(196)
    for (const corner of c) expect(ring.some((p) => p.latitude === corner.latitude && p.longitude === corner.longitude)).toBe(true)
  })

  it('accepts a ring that repeats its first corner, as the API carries it', () => {
    const { ring } = simplifyRing(closeRing(circle(600, 200)))

    expect(ring[0]).not.toEqual(ring[ring.length - 1])
    expect(ring.length).toBeGreaterThanOrEqual(3)
  })

  it('never returns fewer than 3 corners, however loose the tolerance', () => {
    const { ring } = simplifyRing(circle(600, 200), 10_000)

    expect(ring.length).toBeGreaterThanOrEqual(3)
    expect(validateRing(ring)).toBeNull()
  })

  it('handles a 2,000-corner ring quickly', () => {
    const start = performance.now()
    const { ring } = simplifyRing(circle(2_000, 300))

    expect(performance.now() - start).toBeLessThan(1_000)
    expect(ring.length).toBeLessThan(200)
  })
})

describe('simplifyDenseRing (an outline traced from pixels)', () => {
  /** A long rotated rectangle traced on a 1 m pixel grid: every corner is one step of a staircase. */
  function traced(): GeoPoint[] {
    const angle = (-20 * Math.PI) / 180
    const inside = (x: number, y: number) => {
      const u = x * Math.cos(angle) + y * Math.sin(angle)
      const v = -x * Math.sin(angle) + y * Math.cos(angle)
      return u >= 0 && u <= 300 && v >= 0 && v <= 60
    }
    const cells = new Set<string>()
    for (let x = -40; x < 340; x++) for (let y = -40; y < 340; y++) if (inside(x + 0.5, y + 0.5)) cells.add(`${x},${y}`)

    const key = (x: number, y: number) => `${x},${y}`
    const next = new Map<string, [number, number]>()
    for (const c of cells) {
      const [x, y] = c.split(',').map(Number)
      if (!cells.has(key(x, y - 1))) next.set(key(x, y), [x + 1, y])
      if (!cells.has(key(x + 1, y))) next.set(key(x + 1, y), [x + 1, y + 1])
      if (!cells.has(key(x, y + 1))) next.set(key(x + 1, y + 1), [x, y + 1])
      if (!cells.has(key(x - 1, y))) next.set(key(x, y + 1), [x, y])
    }
    const start = [...next.keys()][0]
    const ring: GeoPoint[] = []
    let k = start
    do {
      const [x, y] = k.split(',').map(Number)
      ring.push(point(x, y))
      const n = next.get(k)!
      k = key(n[0], n[1])
    } while (k !== start)
    return ring
  }

  it('is a genuinely dense, valid starting point', () => {
    const ring = traced()

    expect(ring.length).toBeGreaterThan(500)
    expect(validateRing(ring)).toBeNull()
  })

  it('a half-metre tolerance alone leaves most of the staircase in place', () => {
    const ring = traced()

    expect(simplifyRing(ring, 0.5).ring.length).toBeGreaterThan(200)
  })

  it('raises the tolerance until the ring is down to a workable number of corners', () => {
    const ring = traced()
    const result = simplifyDenseRing(ring)

    expect(result.ring.length).toBeLessThanOrEqual(100)
    expect(result.toleranceMeters).toBe(1)
    expect(validateRing(result.ring)).toBeNull()
  })

  it('never lets the shape move by more than a metre', () => {
    const ring = traced()
    const { ring: simplified, toleranceMeters } = simplifyDenseRing(ring)

    expect(toleranceMeters).toBeLessThanOrEqual(1)
    expect(Math.abs(ringAreaSquareMeters(simplified) - ringAreaSquareMeters(ring)) / ringAreaSquareMeters(ring)).toBeLessThan(0.03)
  })

  it('stops at the tight tolerance when that already gets the ring down far enough', () => {
    const result = simplifyDenseRing(Array.from({ length: 600 }, (_, i) => point(200 * Math.cos((2 * Math.PI * i) / 600), 200 * Math.sin((2 * Math.PI * i) / 600))))

    expect(result.toleranceMeters).toBe(SIMPLIFY_TOLERANCE_METERS)
    expect(result.ring.length).toBeLessThanOrEqual(100)
  })

  it('leaves an ordinary ring exactly as it is, reporting no tolerance', () => {
    const ring = [point(0, 0), point(100, 0), point(100, 100), point(0, 100)]
    const result = simplifyDenseRing(ring)

    expect(result.ring).toEqual(ring)
    expect(result.removed).toBe(0)
    expect(result.toleranceMeters).toBe(0)
  })
})
