import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { SiteBoundaryConfidenceBadge } from './SiteBoundaryConfidenceBadge'
import { useActiveSiteBoundaryStore } from '../../../store/activeSiteBoundaryStore'

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

describe('SiteBoundaryConfidenceBadge', () => {
  afterEach(() => {
    useActiveSiteBoundaryStore.getState().clearBoundary()
  })

  it('renders nothing when no boundary is active', () => {
    const { container } = render(<SiteBoundaryConfidenceBadge />)
    expect(container).toBeEmptyDOMElement()
  })

  it('shows the site name, confidence label, and source once a boundary is active', () => {
    act(() => useActiveSiteBoundaryStore.getState().setBoundary(sampleBoundary))
    render(<SiteBoundaryConfidenceBadge />)

    expect(screen.getByText('Al Safa Park 2')).toBeInTheDocument()
    expect(screen.getByText('High confidence')).toBeInTheDocument()
    expect(screen.getByText(/OpenStreetMap \(leisure=park\)/)).toBeInTheDocument()
  })

  // FR-006/WCAG 2.1 AA — confidence must be distinguishable by text/icon, not color alone.
  it.each([
    ['low', 'Low confidence — approximate'],
    ['medium', 'Medium confidence'],
    ['high', 'High confidence'],
  ] as const)('states the %s confidence level as text, not just a color', (level, expectedLabel) => {
    act(() => useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, confidenceLevel: level }))
    render(<SiteBoundaryConfidenceBadge />)

    expect(screen.getByText(expectedLabel)).toBeInTheDocument()
  })

  it('discloses similarly-plausible alternative candidates (FR-008)', () => {
    act(() =>
      useActiveSiteBoundaryStore.getState().setBoundary({
        ...sampleBoundary,
        alternativeCandidateNames: ['Al Safa Park (Landuse)'],
      }),
    )
    render(<SiteBoundaryConfidenceBadge />)

    expect(screen.getByText(/Al Safa Park \(Landuse\)/)).toBeInTheDocument()
  })
})
