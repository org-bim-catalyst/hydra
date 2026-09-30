import { afterEach, describe, expect, it } from 'vitest'
import { siteRingsOf, useActiveSiteBoundaryStore } from './activeSiteBoundaryStore'

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

  describe('specs/079 revision and hand-edit flag', () => {
    it('defaults to no revision and not hand-edited', () => {
      useActiveSiteBoundaryStore.getState().setBoundary(sampleBoundary)
      const s = useActiveSiteBoundaryStore.getState()
      expect(s.revision).toBeNull()
      expect(s.isHandEdited).toBe(false)
    })

    it('stores the revision and the hand-edited flag when given', () => {
      useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, revision: 'rev-1', isHandEdited: true, source: 'UserCorrected' })
      const s = useActiveSiteBoundaryStore.getState()
      expect(s.revision).toBe('rev-1')
      expect(s.isHandEdited).toBe(true)
      expect(s.source).toBe('UserCorrected')
    })

    it('replaces the revision wholesale with the next boundary, never keeping the old one', () => {
      useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, revision: 'rev-1', isHandEdited: true })
      useActiveSiteBoundaryStore.getState().setBoundary(sampleBoundary)
      const s = useActiveSiteBoundaryStore.getState()
      expect(s.revision).toBeNull()
      expect(s.isHandEdited).toBe(false)
    })

    it('clears both on clearBoundary', () => {
      useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, revision: 'rev-1', isHandEdited: true })
      useActiveSiteBoundaryStore.getState().clearBoundary()
      const s = useActiveSiteBoundaryStore.getState()
      expect(s.revision).toBeNull()
      expect(s.isHandEdited).toBe(false)
    })
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

  describe('clearUnlessShowing (specs/077)', () => {
    it("keeps the outline this conversation drew of the site it just confirmed again", () => {
      useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, chatId: 'chat-1' })

      useActiveSiteBoundaryStore.getState().clearUnlessShowing('Al Safa Park 2', 'chat-1')

      expect(useActiveSiteBoundaryStore.getState().polygon).toHaveLength(3)
    })

    it("clears the same site's outline when another conversation drew it", () => {
      // A fresh chat asking for the site again must not show the buildings an earlier chat chose.
      useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, chatId: 'chat-1' })

      useActiveSiteBoundaryStore.getState().clearUnlessShowing('Al Safa Park 2', 'chat-2')

      expect(useActiveSiteBoundaryStore.getState().polygon).toBeNull()
    })

    it("clears another site's outline", () => {
      useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, chatId: 'chat-1' })

      useActiveSiteBoundaryStore.getState().clearUnlessShowing('Zabeel Park', 'chat-1')

      expect(useActiveSiteBoundaryStore.getState().siteName).toBeNull()
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

// specs/077 — a site whose chosen buildings stand apart (a tower across the street) is several rings.
describe('siteRingsOf', () => {
  it('is empty with no site', () => {
    expect(siteRingsOf(useActiveSiteBoundaryStore.getState())).toEqual([])
  })

  it('lists the outline first, then every separate building', () => {
    const tower = [
      { latitude: 25.158, longitude: 55.221 },
      { latitude: 25.158, longitude: 55.2215 },
      { latitude: 25.1575, longitude: 55.2215 },
    ]
    useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, additionalPolygons: [tower] })

    expect(siteRingsOf(useActiveSiteBoundaryStore.getState())).toEqual([sampleBoundary.polygon, tower])
  })

  it('forgets the separate buildings when a site without them replaces it', () => {
    useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, additionalPolygons: [sampleBoundary.polygon] })
    useActiveSiteBoundaryStore.getState().setBoundary(sampleBoundary)

    expect(useActiveSiteBoundaryStore.getState().additionalPolygons).toEqual([])
  })
})
