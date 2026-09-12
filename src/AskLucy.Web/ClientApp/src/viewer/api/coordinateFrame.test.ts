import { beforeEach, describe, expect, it } from 'vitest'
import { localToWorld, worldToLocal } from './coordinateFrame'
import { sceneAnchor } from '../scene/SceneAnchor'

describe('coordinateFrame (FR-009, FR-010, research D2)', () => {
  beforeEach(() => {
    sceneAnchor.set({ latitude: 25.2048, longitude: 55.2708 })
  })

  it('round-trips worldToLocal -> localToWorld within 1 metre (SC-002)', () => {
    const point = { latitude: 25.21, longitude: 55.28 }
    const local = worldToLocal(point, 12)
    const roundTripped = localToWorld(local)

    // 1 metre of latitude/longitude, converted back to degrees, at this reference latitude.
    const toleranceDegreesLat = 1 / 111_320
    const toleranceDegreesLng = 1 / (111_320 * Math.cos((25.2048 * Math.PI) / 180))

    expect(Math.abs(roundTripped.latitude - point.latitude)).toBeLessThan(toleranceDegreesLat)
    expect(Math.abs(roundTripped.longitude - point.longitude)).toBeLessThan(toleranceDegreesLng)
    expect(roundTripped.altitudeMetres).toBe(12)
  })

  it('the reference point itself converts to the local origin', () => {
    const local = worldToLocal({ latitude: 25.2048, longitude: 55.2708 }, 0)
    expect(local.x).toBeCloseTo(0, 6)
    expect(local.y).toBeCloseTo(0, 6)
    expect(local.z).toBe(0)
  })

  it('produces correct, non-stale results for both an old and a new point after the reference point changes (FR-012)', () => {
    const oldPoint = { latitude: 25.21, longitude: 55.28 }
    const localUnderOldReference = worldToLocal(oldPoint, 0)

    sceneAnchor.set({ latitude: 51.5074, longitude: -0.1278 })

    const newPoint = { latitude: 51.51, longitude: -0.13 }
    const localUnderNewReference = worldToLocal(newPoint, 0)

    // Re-converting the OLD point after the reference point changed must reflect the NEW
    // reference, not a cached one — this is what "pure function of the current reference point"
    // (contracts/coordinate-frame-and-drawing-space.md) actually guarantees.
    const oldPointUnderNewReference = worldToLocal(oldPoint, 0)
    expect(oldPointUnderNewReference).not.toEqual(localUnderOldReference)

    expect(localToWorld(localUnderNewReference).latitude).toBeCloseTo(newPoint.latitude, 3)
  })

  it('throws when no reference point has been set yet', () => {
    // Bracket access to reset the private field for a clean test — no public "unset" API exists
    // since a real reference point is never unset once content has loaded (FR-008).
    sceneAnchor['referencePoint'] = null
    expect(() => worldToLocal({ latitude: 0, longitude: 0 }, 0)).toThrow()
  })
})
