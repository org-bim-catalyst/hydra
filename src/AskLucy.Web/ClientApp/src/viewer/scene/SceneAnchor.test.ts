import { describe, expect, it } from 'vitest'
import { localToWorld, worldToLocal } from '../api/coordinateFrame'
import { sceneAnchor } from './SceneAnchor'

describe('SceneAnchor (FR-008)', () => {
  it('returns null before any content has loaded', () => {
    // Module-level singleton — if an earlier test in this file already set it, this file's own
    // first assertion still holds since no other test in this suite calls set() before it.
    expect(sceneAnchor.get()).toBeNull()
  })

  it('holds exactly one reference point at a time', () => {
    sceneAnchor.set({ latitude: 25.2048, longitude: 55.2708 })
    expect(sceneAnchor.get()).toEqual({ latitude: 25.2048, longitude: 55.2708 })

    sceneAnchor.set({ latitude: 51.5074, longitude: -0.1278 })
    expect(sceneAnchor.get()).toEqual({ latitude: 51.5074, longitude: -0.1278 })
  })

  it('T040 (US2 AC5, FR-012): content placed far from the reference point remains correctly positioned after the reference point changes', () => {
    sceneAnchor.set({ latitude: 25.2048, longitude: 55.2708 })
    const nearby = { latitude: 25.21, longitude: 55.28 }
    const localUnderFirstReference = worldToLocal(nearby, 0)

    // Second, distant piece of content moves the reference point — London, thousands of km away.
    sceneAnchor.set({ latitude: 51.5074, longitude: -0.1278 })

    // The FIRST content's world coordinates, re-converted under the NEW reference point, still
    // round-trip correctly — there is no stale cached local position anywhere.
    const localUnderSecondReference = worldToLocal(nearby, 0)
    expect(localUnderSecondReference).not.toEqual(localUnderFirstReference)
    const roundTripped = localToWorld(localUnderSecondReference)
    expect(roundTripped.latitude).toBeCloseTo(nearby.latitude, 6)
    expect(roundTripped.longitude).toBeCloseTo(nearby.longitude, 6)
  })
})
