import { describe, expect, it } from 'vitest'
import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import { closeRing, openRing, ringAreaSquareMeters, validateChange, validateRing } from './ringGeometry'

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
