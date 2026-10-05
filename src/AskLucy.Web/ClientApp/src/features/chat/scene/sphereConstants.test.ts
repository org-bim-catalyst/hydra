import { describe, expect, it } from 'vitest'
import {
  DEFAULT_SPHERE_LOOK,
  SPHERE_CARD_FILL,
  SPHERE_LOOK_LIMITS,
  sphereCameraDistance,
  sphereZoomDistances,
} from './sphereConstants'

describe('the presence sphere look', () => {
  it('defaults to the look the sphere had before it was adjustable (FR-018)', () => {
    expect(DEFAULT_SPHERE_LOOK).toEqual({ dotSizeMultiplier: 1, cardFillPercent: 75, zoomEnabled: false })
    // Filling 75% of the card puts the camera where it was put when that size was chosen.
    expect(sphereCameraDistance(DEFAULT_SPHERE_LOOK.cardFillPercent / 100)).toBe(sphereCameraDistance())
    expect(DEFAULT_SPHERE_LOOK.cardFillPercent / 100).toBe(SPHERE_CARD_FILL)
  })

  it('keeps the limits the backend enforces', () => {
    expect(SPHERE_LOOK_LIMITS.dotSizeMultiplier).toMatchObject({ min: 0.25, max: 2 })
    expect(SPHERE_LOOK_LIMITS.cardFillPercent).toMatchObject({ min: 40, max: 95 })
  })

  it('moves the camera closer the more of the card the sphere should fill', () => {
    expect(sphereCameraDistance(0.95)).toBeLessThan(sphereCameraDistance(0.75))
    expect(sphereCameraDistance(0.75)).toBeLessThan(sphereCameraDistance(0.4))
  })

  it('limits zoom to twice the sphere\'s size in and a quarter of it out (FR-004)', () => {
    const base = sphereCameraDistance(0.75)
    const { minDistance, maxDistance } = sphereZoomDistances(base)

    // Apparent size is inversely proportional to the camera's distance.
    expect(base / minDistance).toBeCloseTo(2)
    expect(base / maxDistance).toBeCloseTo(0.25)
    expect(minDistance).toBeLessThan(base)
    expect(maxDistance).toBeGreaterThan(base)
  })
})
