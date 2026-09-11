import { afterEach, describe, expect, it, vi } from 'vitest'
import { isBuildingsOnlyStyleSupported } from './commands'

describe('isBuildingsOnlyStyleSupported (specs/048-buildings-only-map-style FR-006, research.md Decision 2)', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('returns true when no Map ID is configured (raster rendering)', () => {
    vi.stubEnv('VITE_GOOGLE_MAPS_MAP_ID', '')
    expect(isBuildingsOnlyStyleSupported()).toBe(true)
  })

  it('returns false when a Map ID is configured (vector rendering)', () => {
    vi.stubEnv('VITE_GOOGLE_MAPS_MAP_ID', 'a-real-vector-map-id')
    expect(isBuildingsOnlyStyleSupported()).toBe(false)
  })
})
