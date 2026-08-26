import { afterEach, describe, expect, it } from 'vitest'
import { useActiveSiteBoundaryStore } from './activeSiteBoundaryStore'

const sampleBoundary = {
  siteName: 'Al Safa Park 2',
  centroid: { latitude: 25.156, longitude: 55.2218 },
  polygon: [
    { latitude: 25.156, longitude: 55.221 },
    { latitude: 25.156, longitude: 55.222 },
    { latitude: 25.155, longitude: 55.222 },
  ],
  areaSquareMeters: 15000,
  confidence: 0.92,
  confidenceLevel: 'high' as const,
  source: 'OsmBoundary' as const,
  sourceDetail: 'OpenStreetMap (leisure=park)',
  alternativeCandidateNames: [],
}

afterEach(() => {
  useActiveSiteBoundaryStore.getState().clearBoundary()
})

describe('activeSiteBoundaryStore', () => {
  it('starts with no active boundary', () => {
    const s = useActiveSiteBoundaryStore.getState()
    expect(s.siteName).toBeNull()
    expect(s.polygon).toBeNull()
    expect(s.confidenceLevel).toBeNull()
    expect(s.alternativeCandidateNames).toEqual([])
  })

  describe('setBoundary', () => {
    it('populates all fields', () => {
      useActiveSiteBoundaryStore.getState().setBoundary(sampleBoundary)
      const s = useActiveSiteBoundaryStore.getState()
      expect(s.siteName).toBe('Al Safa Park 2')
      expect(s.polygon).toHaveLength(3)
      expect(s.confidenceLevel).toBe('high')
      expect(s.source).toBe('OsmBoundary')
    })

    it('replaces the previous boundary wholesale — never a partial merge (a new site fully supersedes the previous one)', () => {
      useActiveSiteBoundaryStore.getState().setBoundary(sampleBoundary)
      useActiveSiteBoundaryStore.getState().setBoundary({
        ...sampleBoundary,
        siteName: 'Zabeel Park',
        confidenceLevel: 'low',
        alternativeCandidateNames: ['Zabeel Park (Landuse)'],
      })
      const s = useActiveSiteBoundaryStore.getState()
      expect(s.siteName).toBe('Zabeel Park')
      expect(s.confidenceLevel).toBe('low')
      expect(s.alternativeCandidateNames).toEqual(['Zabeel Park (Landuse)'])
    })
  })

  describe('clearBoundary', () => {
    it('resets all fields to null (edge case: a new, unrelated site must not leave the old one overlaid)', () => {
      useActiveSiteBoundaryStore.getState().setBoundary(sampleBoundary)
      useActiveSiteBoundaryStore.getState().clearBoundary()
      const s = useActiveSiteBoundaryStore.getState()
      expect(s.siteName).toBeNull()
      expect(s.polygon).toBeNull()
      expect(s.confidenceLevel).toBeNull()
      expect(s.alternativeCandidateNames).toEqual([])
    })
  })
})
