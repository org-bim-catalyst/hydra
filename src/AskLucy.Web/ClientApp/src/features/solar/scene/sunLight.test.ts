import * as THREE from 'three'
import { describe, expect, it } from 'vitest'
import { solarPosition, solarPositionToEnuUnitVector } from '../solar/solarPosition'
import { SunLight } from './sunLight'

describe('SunLight — direction and shadow-casting behaviour (FR-016, FR-017)', () => {
  it('aims the light along the sun ENU unit vector, scaled by SUN_LIGHT_DISTANCE_METRES', () => {
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

  it('configures the orthographic shadow camera frustum to radius * 1.3', () => {
    const light = new SunLight(200, 30)
    const camera = light.directionalLight.shadow.camera as THREE.OrthographicCamera
    expect(camera.right).toBeCloseTo(260, 5)
    expect(camera.left).toBeCloseTo(-260, 5)
    expect(camera.top).toBeCloseTo(260, 5)
    expect(camera.bottom).toBeCloseTo(-260, 5)
  })
})
