import { beforeEach, describe, expect, it } from 'vitest'
import { useActiveLocationStore } from '../../store/activeLocationStore'
import { sceneAnchor } from '../scene/SceneAnchor'
import './anchorFollowsActiveLocation'

const BADR_EGYPT = { latitude: 30.1383, longitude: 31.7108 }
const AL_SAFA_PARK_2 = { latitude: 25.1558327, longitude: 55.2217644 }

describe('anchorFollowsActiveLocation', () => {
  beforeEach(() => {
    useActiveLocationStore.getState().clear()
  })

  // Found live (2026-09-14): the reference point stayed at the startup location, so the dome and
  // boundary ring were drawn near Badr, Egypt while the camera was on the Dubai site Lucy confirmed.
  it('moves the reference point to an agent-confirmed site, away from the startup location', () => {
    useActiveLocationStore.getState().setFromGeolocation(BADR_EGYPT.latitude, BADR_EGYPT.longitude)
    expect(sceneAnchor.get()).toEqual(BADR_EGYPT)

    useActiveLocationStore.getState().setFromAgent(AL_SAFA_PARK_2.latitude, AL_SAFA_PARK_2.longitude, 'Al Safa Park 2', 0.92)

    expect(sceneAnchor.get()).toEqual(AL_SAFA_PARK_2)
  })

  it('does not move the reference point when only non-coordinate details change', () => {
    useActiveLocationStore.getState().setFromAgent(AL_SAFA_PARK_2.latitude, AL_SAFA_PARK_2.longitude, 'Al Safa Park 2', 0.92)
    const version = sceneAnchor.version

    useActiveLocationStore.getState().setLocationName(AL_SAFA_PARK_2.latitude, AL_SAFA_PARK_2.longitude, 'Dubai')

    expect(sceneAnchor.version).toBe(version)
  })

  it('keeps the last reference point when the location becomes unavailable', () => {
    useActiveLocationStore.getState().setFromAgent(AL_SAFA_PARK_2.latitude, AL_SAFA_PARK_2.longitude, 'Al Safa Park 2', 0.92)

    useActiveLocationStore.getState().clear()

    expect(sceneAnchor.get()).toEqual(AL_SAFA_PARK_2)
  })
})
