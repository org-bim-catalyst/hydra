import { describe, expect, it } from 'vitest'
import { DEFAULT_SITE_BUILDINGS_RADIUS_METRES, siteBuildingsRadiusFor } from './siteBuildingsRadius'

const SITE = { latitude: 25.2545, longitude: 55.3035 }
const METRES_PER_DEGREE = 111_320

/** A closed square ring whose corners sit `halfSideMetres` north/south and east/west of the site. */
function squareAround(halfSideMetres: number) {
  const dLat = halfSideMetres / METRES_PER_DEGREE
  const dLon = halfSideMetres / (METRES_PER_DEGREE * Math.cos((SITE.latitude * Math.PI) / 180))
  const corners = [
    { latitude: SITE.latitude + dLat, longitude: SITE.longitude - dLon },
    { latitude: SITE.latitude + dLat, longitude: SITE.longitude + dLon },
    { latitude: SITE.latitude - dLat, longitude: SITE.longitude + dLon },
    { latitude: SITE.latitude - dLat, longitude: SITE.longitude - dLon },
  ]
  return [...corners, corners[0]]
}

describe('siteBuildingsRadiusFor (specs/076)', () => {
  it("keeps the server's default when there is no boundary", () => {
    expect(siteBuildingsRadiusFor(SITE, null)).toBe(DEFAULT_SITE_BUILDINGS_RADIUS_METRES)
    expect(siteBuildingsRadiusFor(SITE, [SITE, SITE])).toBe(DEFAULT_SITE_BUILDINGS_RADIUS_METRES)
  })

  it('never asks for less than the default for a small site', () => {
    expect(siteBuildingsRadiusFor(SITE, squareAround(30))).toBe(DEFAULT_SITE_BUILDINGS_RADIUS_METRES)
  })

  it("reaches the boundary's furthest corner plus a margin of neighbours, rounded up to 50 m", () => {
    // Corners are 200·√2 ≈ 283 m away; + 100 m margin = 383 m → 400 m.
    expect(siteBuildingsRadiusFor(SITE, squareAround(200))).toBe(400)
  })

  it('is capped at 500 m however large the boundary', () => {
    expect(siteBuildingsRadiusFor(SITE, squareAround(2000))).toBe(500)
  })
})
