import { describe, expect, it } from 'vitest'
import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import { ringAreaSquareMeters, toLocalMeters, validateRing } from './ringGeometry'
import { arcThroughPoint, circleRing, cornersInBox, curveEdge, equivalentRadius, ringCentre, roundCorner } from './ringShapes'

const LAT = 23.59
const LON = 58.4
const P = (east: number, north: number): GeoPoint => ({
  latitude: LAT + north / 111_320,
  longitude: LON + east / (111_320 * Math.cos((LAT * Math.PI) / 180)),
})

/** A 100 x 100 m square, counter-clockwise. */
const square = (): GeoPoint[] => [P(0, 0), P(100, 0), P(100, 100), P(0, 100)]
/** The same square, clockwise. */
const squareClockwise = (): GeoPoint[] => [...square()].reverse()

type Result = { ring: GeoPoint[] } | { refusal: string }
const ringOf = (result: Result): GeoPoint[] => {
  if (!('ring' in result)) throw new Error(result.refusal)
  return result.ring
}
const refusalOf = (result: Result): string => ('refusal' in result ? result.refusal : '')

/** Distance in metres between two points. */
const metres = (a: GeoPoint, b: GeoPoint) => {
  const [pa, pb] = toLocalMeters([a, b], a)
  return Math.hypot(pb.x - pa.x, pb.y - pa.y)
}

describe('roundCorner', () => {
  it('replaces the corner with an arc, keeping the other corners', () => {
    const ring = ringOf(roundCorner(square(), 2, 20))

    expect(ring.length).toBeGreaterThan(square().length + 3)
    expect(ring[0]).toEqual(square()[0])
    expect(ring[1]).toEqual(square()[1])
    expect(ring[ring.length - 1]).toEqual(square()[3])
    expect(validateRing(ring)).toBeNull()
  })

  it('starts and ends the curve the radius from the corner along each edge', () => {
    const ring = ringOf(roundCorner(square(), 2, 20))

    // A right angle: the tangent points sit exactly `radius` metres from the corner along each edge.
    expect(metres(ring[2], P(100, 100))).toBeCloseTo(20, 0)
    expect(metres(ring[ring.length - 2], P(100, 100))).toBeCloseTo(20, 0)
  })

  it('takes the expected area off a right-angled corner: r squared times (1 - pi/4)', () => {
    const before = ringAreaSquareMeters(square())
    const after = ringAreaSquareMeters(ringOf(roundCorner(square(), 2, 20)))

    expect(before - after).toBeCloseTo(20 * 20 * (1 - Math.PI / 4), -1)
  })

  it('puts every point of the curve the radius from the curve centre', () => {
    const ring = ringOf(roundCorner(square(), 2, 20))
    const curve = toLocalMeters(ring.slice(2, ring.length - 1), square()[0])

    for (const point of curve) expect(Math.hypot(point.x - 80, point.y - 80)).toBeCloseTo(20, 0)
  })

  it('works the same on a clockwise ring', () => {
    const ring = ringOf(roundCorner(squareClockwise(), 1, 20))

    expect(validateRing(ring)).toBeNull()
    expect(ringAreaSquareMeters(square()) - ringAreaSquareMeters(ring)).toBeCloseTo(20 * 20 * (1 - Math.PI / 4), -1)
  })

  it('refuses a radius too big for the corner, and says the largest that fits', () => {
    expect(refusalOf(roundCorner(square(), 2, 80))).toMatch(/too big.*most that fits is 50 m/)
  })

  it('refuses a corner that is already straight', () => {
    const straight = [P(0, 0), P(50, 0), P(100, 0), P(100, 100), P(0, 100)]

    expect(refusalOf(roundCorner(straight, 1, 5))).toMatch(/already straight/)
  })

  it('refuses a non-positive radius and a missing corner', () => {
    expect(refusalOf(roundCorner(square(), 2, 0))).not.toBe('')
    expect(refusalOf(roundCorner(square(), 2, -5))).not.toBe('')
    expect(refusalOf(roundCorner(square(), 9, 5))).not.toBe('')
  })

  it('rounds the first and last corners across the ring seam', () => {
    expect(validateRing(ringOf(roundCorner(square(), 0, 15)))).toBeNull()
    expect(validateRing(ringOf(roundCorner(square(), 3, 15)))).toBeNull()
  })
})

describe('curveEdge', () => {
  it('bends the edge outward: a positive bulge adds area on a counter-clockwise ring', () => {
    const ring = ringOf(curveEdge(square(), 0, 10))

    expect(ring.length).toBeGreaterThan(square().length)
    expect(ringAreaSquareMeters(ring)).toBeGreaterThan(ringAreaSquareMeters(square()))
    expect(validateRing(ring)).toBeNull()
  })

  it('bends it inward for a negative bulge', () => {
    const ring = ringOf(curveEdge(square(), 0, -10))

    expect(ringAreaSquareMeters(ring)).toBeLessThan(ringAreaSquareMeters(square()))
    expect(validateRing(ring)).toBeNull()
  })

  it('treats outward the same way on a clockwise ring', () => {
    const outward = ringOf(curveEdge(squareClockwise(), 2, 10))

    expect(ringAreaSquareMeters(outward)).toBeGreaterThan(ringAreaSquareMeters(square()))
  })

  it('reaches the bulge at the middle of the edge', () => {
    const ring = ringOf(curveEdge(square(), 0, 10))
    const lowest = Math.min(...toLocalMeters(ring, square()[0]).map((p) => p.y))

    expect(lowest).toBeCloseTo(-10, 0)
  })

  it('keeps the original corners and only adds new ones between the ends of the edge', () => {
    const ring = ringOf(curveEdge(square(), 0, 10))

    expect(ring[0]).toEqual(square()[0])
    expect(ring[ring.length - 3]).toEqual(square()[1])
    expect(ring).toContainEqual(square()[2])
    expect(ring).toContainEqual(square()[3])
  })

  it('curves the closing edge, from the last corner back to the first', () => {
    const ring = ringOf(curveEdge(square(), 3, 10))

    expect(ring.length).toBeGreaterThan(square().length)
    expect(validateRing(ring)).toBeNull()
  })

  it('allows at most a semicircle, and refuses more with the largest value', () => {
    expect('ring' in curveEdge(square(), 0, 50)).toBe(true)
    expect(refusalOf(curveEdge(square(), 0, 60))).toMatch(/too big.*most is 50 m/)
  })

  it('refuses a zero or missing bulge and a missing corner', () => {
    expect(refusalOf(curveEdge(square(), 0, 0))).not.toBe('')
    expect(refusalOf(curveEdge(square(), 0, Number.NaN))).not.toBe('')
    expect(refusalOf(curveEdge(square(), 9, 5))).not.toBe('')
  })
})

describe('circleRing', () => {
  it('makes a circle of the radius, with a matching area', () => {
    const ring = ringOf(circleRing(P(50, 50), 60))

    expect(ring).toHaveLength(72)
    expect(validateRing(ring)).toBeNull()
    expect(ringAreaSquareMeters(ring)).toBeCloseTo(Math.PI * 60 * 60, -2)
  })

  it('is centred where asked', () => {
    const ring = ringOf(circleRing(P(50, 50), 60))

    expect(metres(ringCentre(ring), P(50, 50))).toBeLessThan(0.5)
  })

  it('refuses a non-positive radius', () => {
    expect(refusalOf(circleRing(P(0, 0), 0))).not.toBe('')
  })
})

describe('equivalentRadius and ringCentre', () => {
  it('give the radius of the circle with the same area', () => {
    expect(equivalentRadius(Math.PI * 100 * 100)).toBeCloseTo(100, 6)
  })

  it('centre a square on its middle', () => {
    expect(metres(ringCentre(square()), P(50, 50))).toBeLessThan(0.5)
  })
})

describe('cornersInBox', () => {
  const corners = [P(0, 0), P(10, 0), P(20, 0), P(30, 0)]
  const toPixel = (p: GeoPoint) => {
    const [local] = toLocalMeters([p], P(0, 0))
    return { x: local.x * 10, y: 100 - local.y * 10 }
  }

  it('selects the corners inside the box', () => {
    expect(cornersInBox(corners, toPixel, { left: 50, top: 0, right: 250, bottom: 200 })).toEqual([1, 2])
  })

  it('does not care which way the box was dragged', () => {
    expect(cornersInBox(corners, toPixel, { left: 250, top: 200, right: 50, bottom: 0 })).toEqual([1, 2])
  })

  it('includes corners on the box edge', () => {
    // The box edges sit exactly on corners 1 and 2 (x = 100 and 200), within floating-point rounding.
    expect(cornersInBox(corners, toPixel, { left: 99.999, top: 99.999, right: 200.001, bottom: 100.001 })).toEqual([1, 2])
  })

  it('selects nothing when the box is empty', () => {
    expect(cornersInBox(corners, toPixel, { left: 500, top: 0, right: 600, bottom: 200 })).toEqual([])
  })

  it('skips corners the map cannot place', () => {
    const partial = (p: GeoPoint) => (p === corners[1] ? null : toPixel(p))
    expect(cornersInBox(corners, partial, { left: -10, top: 0, right: 400, bottom: 200 })).toEqual([0, 2, 3])
  })
})

describe('arcThroughPoint (draw an arc by dropping a third point)', () => {
  type ArcOk = { ring: GeoPoint[]; arc: GeoPoint[] }
  const arcOf = (result: ReturnType<typeof arcThroughPoint>): ArcOk => {
    if (!('arc' in result)) throw new Error(result.refusal)
    return result
  }

  it('draws an arc that starts and ends on the two corners and passes through the dropped point', () => {
    const { arc } = arcOf(arcThroughPoint(square(), 0, 1, P(50, -20)))

    expect(metres(arc[0], P(0, 0))).toBeLessThan(0.01)
    expect(metres(arc[arc.length - 1], P(100, 0))).toBeLessThan(0.01)
    // Every arc corner lies on one circle, and the dropped point is within a step of the curve.
    const nearest = Math.min(...arc.map((p) => metres(p, P(50, -20))))
    expect(nearest).toBeLessThan(6)
  })

  it('puts every corner of the curve the same distance from one centre', () => {
    const { arc } = arcOf(arcThroughPoint(square(), 0, 1, P(50, -20)))
    const local = toLocalMeters(arc, square()[0])
    // Circle through (0,0), (50,-20), (100,0): centre (50, 52.5), radius 72.5.
    for (const point of local) expect(Math.hypot(point.x - 50, point.y - 52.5)).toBeCloseTo(72.5, 0)
  })

  it('swaps the edge for the curve, keeping every other corner', () => {
    const { ring } = arcOf(arcThroughPoint(square(), 0, 1, P(50, -20)))

    expect(ring.length).toBeGreaterThan(square().length)
    expect(ring[0]).toEqual(square()[0])
    expect(ring).toContainEqual(square()[2])
    expect(ring).toContainEqual(square()[3])
    expect(validateRing(ring)).toBeNull()
  })

  it('bulges the way the point was dropped: outward adds area, inward takes it away', () => {
    const outward = arcOf(arcThroughPoint(square(), 0, 1, P(50, -20))).ring
    const inward = arcOf(arcThroughPoint(square(), 0, 1, P(50, 20))).ring

    expect(ringAreaSquareMeters(outward)).toBeGreaterThan(ringAreaSquareMeters(square()))
    expect(ringAreaSquareMeters(inward)).toBeLessThan(ringAreaSquareMeters(square()))
  })

  it('the order the two corners were picked does not change the result', () => {
    const forward = arcOf(arcThroughPoint(square(), 0, 1, P(50, -20))).ring
    const backward = arcOf(arcThroughPoint(square(), 1, 0, P(50, -20))).ring

    expect(ringAreaSquareMeters(backward)).toBeCloseTo(ringAreaSquareMeters(forward), 1)
  })

  it('replaces the corners in between when the two chosen corners are not neighbours', () => {
    // Corners 0 and 2 of a 6-corner ring; the run 0-1-2 holds one corner, the other run holds three.
    const ring6 = [P(0, 0), P(50, -10), P(100, 0), P(100, 100), P(50, 110), P(0, 100)]

    const { ring } = arcOf(arcThroughPoint(ring6, 0, 2, P(50, -30)))

    expect(ring).not.toContainEqual(ring6[1])
    expect(ring).toContainEqual(ring6[3])
    expect(ring).toContainEqual(ring6[4])
    expect(ring).toContainEqual(ring6[5])
    expect(validateRing(ring)).toBeNull()
  })

  it('takes the shorter run even when it wraps across the start of the ring', () => {
    const ring6 = [P(0, 0), P(50, -10), P(100, 0), P(100, 100), P(50, 110), P(0, 100)]

    // Corners 5 and 1: the short run is 5-0-1, through the start of the list.
    const { ring } = arcOf(arcThroughPoint(ring6, 5, 1, P(-30, 40)))

    expect(ring).not.toContainEqual(ring6[0])
    expect(ring).toContainEqual(ring6[2])
    expect(ring).toContainEqual(ring6[3])
    expect(ring).toContainEqual(ring6[4])
  })

  it('refuses three points in a straight line, and says how to fix it', () => {
    const result = arcThroughPoint(square(), 0, 1, P(50, 0))

    expect('refusal' in result && result.refusal).toMatch(/straight line/)
  })

  it('refuses an arc that is almost straight', () => {
    const result = arcThroughPoint(square(), 0, 1, P(50, -0.0001))

    expect('refusal' in result).toBe(true)
  })

  it('refuses the same corner twice and a corner that does not exist', () => {
    expect('refusal' in arcThroughPoint(square(), 1, 1, P(50, -20))).toBe(true)
    expect('refusal' in arcThroughPoint(square(), 0, 9, P(50, -20))).toBe(true)
  })

  it('works on a clockwise ring too', () => {
    const ring = arcOf(arcThroughPoint(squareClockwise(), 2, 3, P(50, -20))).ring

    expect(validateRing(ring)).toBeNull()
    expect(ringAreaSquareMeters(ring)).toBeGreaterThan(ringAreaSquareMeters(square()))
  })
})
