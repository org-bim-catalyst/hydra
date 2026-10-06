import { describe, expect, it } from 'vitest'
import { isCounterClockwise } from '../../siteBoundaryEdit/ringGeometry'
import { siteBoundaryBorderRings, siteBoundaryPaths } from './siteBoundaryPaths'

const p = (latitude: number, longitude: number) => ({ latitude, longitude })
const outer = [p(0, 0), p(0, 10), p(10, 10), p(10, 0)]
const hole = [p(4, 4), p(4, 6), p(6, 6), p(6, 4)]
const toRing = (path: google.maps.LatLngLiteral[]) => path.map((c) => p(c.lat, c.lng))

describe('siteBoundaryPaths (specs/081)', () => {
  it('is just the rings when there are no voids', () => {
    const paths = siteBoundaryPaths(outer, [hole])

    expect(paths).toHaveLength(2)
    expect(paths[0][2]).toEqual({ lat: 10, lng: 10 })
  })

  it('adds each void as an inner path after the rings, wound against the ring it is in', () => {
    const paths = siteBoundaryPaths(outer, [], [[hole]])

    expect(paths).toHaveLength(2)
    expect(isCounterClockwise(toRing(paths[1]))).not.toBe(isCounterClockwise(toRing(paths[0])))
  })

  it('winds a void against its own ring whichever way that ring runs', () => {
    const reversedOuter = [...outer].reverse()
    const paths = siteBoundaryPaths(reversedOuter, [], [[hole]])

    expect(isCounterClockwise(toRing(paths[1]))).not.toBe(isCounterClockwise(toRing(paths[0])))
  })

  it('puts the voids of an additional ring after every ring', () => {
    const other = [p(20, 20), p(20, 30), p(30, 30), p(30, 20)]
    const inner = [p(24, 24), p(24, 26), p(26, 26), p(26, 24)]

    const paths = siteBoundaryPaths(outer, [other], [[], [inner]])

    expect(paths).toHaveLength(3)
    expect(paths[2]).toContainEqual({ lat: 24, lng: 24 })
  })

  it('gives every ring and every void its own animated border', () => {
    expect(siteBoundaryBorderRings(outer, [], [[hole]])).toHaveLength(2)
    expect(siteBoundaryBorderRings(outer)).toHaveLength(1)
  })
})
