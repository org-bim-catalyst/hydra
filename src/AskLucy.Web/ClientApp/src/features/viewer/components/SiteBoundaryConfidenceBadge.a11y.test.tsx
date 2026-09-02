import { act, render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { afterEach, describe, expect, it } from 'vitest'
import { SiteBoundaryConfidenceBadge } from './SiteBoundaryConfidenceBadge'
import { useActiveSiteBoundaryStore } from '../../../store/activeSiteBoundaryStore'

expect.extend(toHaveNoViolations)

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

// specs/042-site-boundary-resolution FR-006 / constitution §7 — confidence must be
// distinguishable without relying on color alone; this is a structural a11y check
// (roles/labels), the color-independence itself is asserted textually in
// SiteBoundaryConfidenceBadge.test.tsx.
describe('SiteBoundaryConfidenceBadge accessibility', () => {
  afterEach(() => {
    useActiveSiteBoundaryStore.getState().clearBoundary()
  })

  it('has no automatically detectable a11y violations for a High confidence boundary', async () => {
    act(() => useActiveSiteBoundaryStore.getState().setBoundary(sampleBoundary))
    const { container } = render(<SiteBoundaryConfidenceBadge />)

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations for a Low confidence boundary', async () => {
    act(() => useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, confidenceLevel: 'low' }))
    const { container } = render(<SiteBoundaryConfidenceBadge />)

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
