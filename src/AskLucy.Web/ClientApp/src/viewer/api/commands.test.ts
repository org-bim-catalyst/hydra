import { afterEach, describe, expect, it, vi } from 'vitest'
import { isBuildingsOnlyStyleSupported } from './commands'

describe('isBuildingsOnlyStyleSupported (specs/048-buildings-only-map-style FR-006, research.md Decision 4)', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('returns true when no Map ID is configured (raster rendering — client-side style just works)', () => {
    vi.stubEnv('VITE_GOOGLE_MAPS_MAP_ID', '')
    vi.stubEnv('VITE_GOOGLE_MAPS_BUILDINGS_ONLY_MAP_ID', '')
    expect(isBuildingsOnlyStyleSupported()).toBe(true)
  })

  it('returns false when a Map ID is configured but no buildings-only Map ID exists (vector rendering, no alternate style)', () => {
    vi.stubEnv('VITE_GOOGLE_MAPS_MAP_ID', 'a-real-vector-map-id')
    vi.stubEnv('VITE_GOOGLE_MAPS_BUILDINGS_ONLY_MAP_ID', '')
    expect(isBuildingsOnlyStyleSupported()).toBe(false)
  })

  it('returns true when a Map ID is configured AND a buildings-only Map ID exists (vector rendering, cloud-styled alternate available)', () => {
    vi.stubEnv('VITE_GOOGLE_MAPS_MAP_ID', 'a-real-vector-map-id')
    vi.stubEnv('VITE_GOOGLE_MAPS_BUILDINGS_ONLY_MAP_ID', 'a-buildings-only-map-id')
    expect(isBuildingsOnlyStyleSupported()).toBe(true)
  })
})
