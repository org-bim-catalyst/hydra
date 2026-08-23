import { afterEach, describe, expect, it } from 'vitest'
import { useActiveLocationStore } from './activeLocationStore'

afterEach(() => {
  useActiveLocationStore.getState().clear()
})

describe('activeLocationStore', () => {
  it('starts with no active location', () => {
    const s = useActiveLocationStore.getState()
    expect(s.source).toBeNull()
    expect(s.latitude).toBeNull()
    expect(s.longitude).toBeNull()
    expect(s.locationName).toBeNull()
    expect(s.confidence).toBeNull()
  })

  describe('setFromGeolocation', () => {
    it('sets source to geolocation when no location is active', () => {
      useActiveLocationStore.getState().setFromGeolocation(25.0, 55.0)
      const s = useActiveLocationStore.getState()
      expect(s.source).toBe('geolocation')
      expect(s.latitude).toBe(25.0)
      expect(s.longitude).toBe(55.0)
      expect(s.confidence).toBeNull()
    })

    it('updates coordinates when source is already geolocation (user physically moved)', () => {
      useActiveLocationStore.getState().setFromGeolocation(25.0, 55.0)
      useActiveLocationStore.getState().setFromGeolocation(26.0, 56.0)
      const s = useActiveLocationStore.getState()
      expect(s.latitude).toBe(26.0)
      expect(s.longitude).toBe(56.0)
    })

    it('is a no-op when source is agent (FR-012 priority rule — quickstart.md Scenario 6)', () => {
      useActiveLocationStore.getState().setFromAgent(25.2048, 55.2708, 'Al Safa 2 Park', 0.97)
      useActiveLocationStore.getState().setFromGeolocation(25.0819, 55.1367)
      const s = useActiveLocationStore.getState()
      expect(s.latitude).toBe(25.2048)
      expect(s.longitude).toBe(55.2708)
      expect(s.source).toBe('agent')
    })
  })

  describe('setFromAgent', () => {
    it('sets source to agent and populates all fields', () => {
      useActiveLocationStore.getState().setFromAgent(25.2048, 55.2708, 'Al Safa 2 Park', 0.97)
      const s = useActiveLocationStore.getState()
      expect(s.source).toBe('agent')
      expect(s.latitude).toBe(25.2048)
      expect(s.longitude).toBe(55.2708)
      expect(s.locationName).toBe('Al Safa 2 Park')
      expect(s.confidence).toBe(0.97)
    })

    it('overrides a geolocation-sourced location (agent always wins)', () => {
      useActiveLocationStore.getState().setFromGeolocation(25.0819, 55.1367)
      useActiveLocationStore.getState().setFromAgent(25.2048, 55.2708, 'Al Safa 2 Park', 0.97)
      const s = useActiveLocationStore.getState()
      expect(s.source).toBe('agent')
      expect(s.latitude).toBe(25.2048)
    })

    it('can update an existing agent location with a new one (US3 AC3 — sequential confirmations)', () => {
      useActiveLocationStore.getState().setFromAgent(25.2048, 55.2708, 'Al Safa 2 Park', 0.97)
      useActiveLocationStore.getState().setFromAgent(25.0819, 55.1367, 'Dubai Marina', 0.95)
      const s = useActiveLocationStore.getState()
      expect(s.latitude).toBe(25.0819)
      expect(s.locationName).toBe('Dubai Marina')
      expect(s.confidence).toBe(0.95)
    })
  })

  describe('setLocationName', () => {
    it('updates locationName when coordinates match the active location', () => {
      useActiveLocationStore.getState().setFromGeolocation(25.0, 55.0)
      useActiveLocationStore.getState().setLocationName(25.0, 55.0, 'Dubai')
      expect(useActiveLocationStore.getState().locationName).toBe('Dubai')
    })

    it('ignores a stale weather response landing after location changed (coordinate guard)', () => {
      useActiveLocationStore.getState().setFromGeolocation(25.0, 55.0)
      useActiveLocationStore.getState().setLocationName(25.0, 55.0, 'First Place')
      useActiveLocationStore.getState().setFromGeolocation(26.0, 56.0)
      // Stale weather response for the old location arrives — must not overwrite
      useActiveLocationStore.getState().setLocationName(25.0, 55.0, 'This Should Be Ignored')
      expect(useActiveLocationStore.getState().locationName).toBe('First Place')
    })
  })

  describe('clear', () => {
    it('resets all fields to null', () => {
      useActiveLocationStore.getState().setFromAgent(25.2048, 55.2708, 'Al Safa 2 Park', 0.97)
      useActiveLocationStore.getState().clear()
      const s = useActiveLocationStore.getState()
      expect(s.source).toBeNull()
      expect(s.latitude).toBeNull()
      expect(s.longitude).toBeNull()
      expect(s.locationName).toBeNull()
      expect(s.confidence).toBeNull()
    })

    it('allows geolocation to re-establish after clear (FR-012 revocation recovery)', () => {
      useActiveLocationStore.getState().setFromAgent(25.2048, 55.2708, 'Al Safa 2 Park', 0.97)
      useActiveLocationStore.getState().clear()
      useActiveLocationStore.getState().setFromGeolocation(25.0819, 55.1367)
      const s = useActiveLocationStore.getState()
      expect(s.source).toBe('geolocation')
      expect(s.latitude).toBe(25.0819)
    })
  })
})
