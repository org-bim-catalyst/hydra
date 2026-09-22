import * as THREE from 'three'
import { describe, expect, it } from 'vitest'
import { solarPosition, solarPositionToEnuUnitVector } from '../solar/solarPosition'
import { MIN_SHADOW_ELEVATION_DEGREES, SHADOW_FRUSTUM_MARGIN, SunLight, shadowRadiusMetres } from './sunLight'

describe('SunLight — direction and shadow-casting behaviour (FR-016, FR-017)', () => {
  it('aims the light along the sun ENU unit vector, scaled by the light distance', () => {
    const light = new SunLight(200)
    const az = 247.3
    const alt = 41.8
    light.aimAt(az, alt)

    const unit = solarPositionToEnuUnitVector(az, alt)
    const expectedLength = Math.sqrt(light.directionalLight.position.x ** 2 + light.directionalLight.position.y ** 2 + light.directionalLight.position.z ** 2)
    expect(expectedLength).toBeCloseTo(400, 0)

    const normalized = light.directionalLight.position.clone().normalize()
    expect(normalized.x).toBeCloseTo(unit.x, 5)
    expect(normalized.y).toBeCloseTo(unit.y, 5)
    expect(normalized.z).toBeCloseTo(unit.z, 5)
  })

  it('disables the light and zeroes intensity when the sun is below the horizon (FR-017)', () => {
    const light = new SunLight(200)
    light.aimAt(180, -10)
    expect(light.directionalLight.visible).toBe(false)
    expect(light.directionalLight.intensity).toBe(0)
  })

  it('enables the light when the sun is above the horizon', () => {
    const light = new SunLight(200)
    light.aimAt(180, 45)
    expect(light.directionalLight.visible).toBe(true)
    expect(light.directionalLight.intensity).toBeGreaterThan(0)
  })

  it('matches Dubai 21 June 15:00 local: sun west-northwest, so shadows fall east-southeast', () => {
    // Dubai (Asia/Dubai, UTC+4, no DST): 15:00 local == 11:00 UTC.
    const position = solarPosition(new Date(Date.UTC(2026, 5, 21, 11, 0)), 25.2, 55.3)
    expect(position.azimuthDegrees).toBeGreaterThan(225) // west-of-south through west-northwest
    expect(position.azimuthDegrees).toBeLessThan(315)

    // Shadow direction is the negation of the sun's direction — opposite azimuth (mod 360).
    const shadowAzimuth = (position.azimuthDegrees + 180) % 360
    expect(shadowAzimuth).toBeGreaterThan(45)
    expect(shadowAzimuth).toBeLessThan(135) // east-southeast through east-northeast
  })

  it('configures the orthographic shadow camera frustum to radius * the ground-clearance margin', () => {
    const light = new SunLight(200, 30)
    const camera = light.directionalLight.shadow.camera as THREE.OrthographicCamera
    const expected = 200 * SHADOW_FRUSTUM_MARGIN
    expect(camera.right).toBeCloseTo(expected, 5)
    expect(camera.left).toBeCloseTo(-expected, 5)
    expect(camera.top).toBeCloseTo(expected, 5)
    expect(camera.bottom).toBeCloseTo(-expected, 5)
  })
})

/**
 * T019, FR-014, SC-006 — fitting the frustum to the content is only safe if the fit still reaches
 * the end of the longest shadow that content can cast. These assert that directly, in metres,
 * rather than asserting the formula back to itself.
 */
describe('shadowRadiusMetres — fitting tightly without truncating (T019, FR-014, SC-006)', () => {
  it('reaches the tip of the tallest building’s shadow at any elevation it is asked about', () => {
    const extent = 120
    const tallest = 45

    for (const elevation of [90, 60, 45, 30, 15, 5, 2, MIN_SHADOW_ELEVATION_DEGREES]) {
      const radius = shadowRadiusMetres(extent, tallest, elevation)
      // Worst case: a building standing at the far edge of the content, casting directly outward.
      const longestShadow = tallest / Math.tan((elevation * Math.PI) / 180)
      expect(radius).toBeGreaterThanOrEqual(extent + longestShadow - 1e-9)
    }
  })

  it('reaches further as the sun drops, because the shadow it has to contain gets longer', () => {
    const atNoon = shadowRadiusMetres(120, 45, 70)
    const atMidMorning = shadowRadiusMetres(120, 45, 25)
    const atFloor = shadowRadiusMetres(120, 45, MIN_SHADOW_ELEVATION_DEGREES)

    expect(atNoon).toBeLessThan(atMidMorning)
    expect(atMidMorning).toBeLessThan(atFloor)
  })

  it('fits far tighter than a radius fixed at the floor elevation would, which is the resolution gain (SC-005)', () => {
    // The Badr measurement behind the deviation recorded in shadowRadiusMetres' own documentation:
    // ~200 m of content, tallest 9 m. A radius held at the floor elevation all day is 3x wider than
    // one that tracks the sun, spread over the same 2048-square shadow map.
    const heldAtFloor = shadowRadiusMetres(200, 9, MIN_SHADOW_ELEVATION_DEGREES)
    const atFortyFive = shadowRadiusMetres(200, 9, 45)

    expect(heldAtFloor).toBeGreaterThan(700)
    expect(atFortyFive).toBeLessThan(215)
    expect(heldAtFloor / atFortyFive).toBeGreaterThan(3)
  })
})

/**
 * T020, FR-012 — the low-sun cut-off. Two separate claims: the light stops casting at the same
 * threshold the radius is floored at, and the radius itself stays finite as elevation approaches
 * zero, where `height / tan(elevation)` would otherwise diverge.
 */
describe('Low-sun cut-off (T020, FR-012)', () => {
  it('disables the light at and below the minimum shadow elevation, not only below the horizon', () => {
    const light = new SunLight(200, 30)

    for (const elevation of [-10, -0.5, 0, 0.5, MIN_SHADOW_ELEVATION_DEGREES]) {
      light.aimAt(120, elevation)
      expect(light.directionalLight.visible).toBe(false)
      expect(light.directionalLight.intensity).toBe(0)
    }

    light.aimAt(120, MIN_SHADOW_ELEVATION_DEGREES + 0.5)
    expect(light.directionalLight.visible).toBe(true)
    expect(light.directionalLight.intensity).toBeGreaterThan(0)
  })

  it('keeps the radius finite and bounded as the sun approaches the horizon', () => {
    const atFloor = shadowRadiusMetres(200, 60, MIN_SHADOW_ELEVATION_DEGREES)

    for (const elevation of [1, 0.5, 0.1, 0.001, 0, -5, -30]) {
      const radius = shadowRadiusMetres(200, 60, elevation)
      expect(Number.isFinite(radius)).toBe(true)
      // Floored, not extrapolated: below the cut-off nothing casts, so the radius stops growing.
      expect(radius).toBeCloseTo(atFloor, 6)
    }
  })

  it('keeps the shadow camera frustum finite at the cut-off, so a near-horizon sun cannot produce a degenerate projection', () => {
    const light = new SunLight(shadowRadiusMetres(200, 60, 0), 60)
    const camera = light.directionalLight.shadow.camera as THREE.OrthographicCamera

    expect(Number.isFinite(camera.right)).toBe(true)
    expect(camera.far).toBeGreaterThan(camera.near)
    // Every point of content lies between the light and the far plane, not behind either.
    expect(camera.far).toBeGreaterThan(light.lightDistanceMetres)
  })
})
