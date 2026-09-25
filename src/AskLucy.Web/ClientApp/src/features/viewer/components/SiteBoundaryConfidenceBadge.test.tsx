import { getContrastRatio, ThemeProvider } from '@mui/material'
import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { SiteBoundaryConfidenceBadge } from './SiteBoundaryConfidenceBadge'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { useActiveSiteBoundaryStore } from '../../../store/activeSiteBoundaryStore'
import { createAppTheme } from '../../../theme'

function renderBadge(mode: 'light' | 'dark' = 'light') {
  return render(
    <ThemeProvider theme={createAppTheme(mode)}>
      <SiteBoundaryConfidenceBadge />
    </ThemeProvider>,
  )
}

/** Every inline and class-generated style the badge renders, flattened to one string. */
function renderedStyles(container: HTMLElement): string {
  const inline = Array.from(container.querySelectorAll<HTMLElement>('*'))
    .map((el) => el.getAttribute('style') ?? '')
    .join(' ')
  const sheets = Array.from(document.querySelectorAll('style'))
    .map((el) => el.textContent ?? '')
    .join(' ')
  return `${inline} ${sheets}`
}

/** Normalises a CSS colour to jsdom's computed form, so hex and rgb() compare equal. */
function computedColour(colour: string): string {
  const probe = document.createElement('span')
  probe.style.color = colour
  document.body.appendChild(probe)
  const computed = window.getComputedStyle(probe).color
  probe.remove()
  return computed
}

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
    useActiveLocationStore.getState().clear()
  })

  // Found live 2026-09-25: Lucy said "confirmed ... with a high confidence level. Now highlighting
  // the boundary." and the card stayed away until the outline — which a deploy restart then cut
  // short, so it never came at all.
  it('appears as soon as Lucy confirms the place, before any boundary resolves', () => {
    act(() =>
      useActiveLocationStore.getState().setFromAgent(25.1558, 55.2218, 'Alsafa Park 2', 0.9, 'ROOFTOP', null, 'high'),
    )
    renderBadge()

    const badge = screen.getByRole('status')
    expect(badge).toHaveAccessibleName('Alsafa Park 2 location: High confidence')
    expect(badge).toHaveTextContent(/^High confidenceAlsafa Park 2$/)
  })

  it("switches to the boundary's own level once the outline lands", () => {
    act(() =>
      useActiveLocationStore.getState().setFromAgent(25.1558, 55.2218, 'Al Safa Park 2', 0.9, 'ROOFTOP', null, 'high'),
    )
    renderBadge()

    act(() => useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, confidenceLevel: 'medium' }))

    expect(screen.getByRole('status')).toHaveAccessibleName('Al Safa Park 2 boundary: Medium confidence')
  })

  it('renders nothing for a device-location fix, which Lucy never confirmed', () => {
    act(() => useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278))
    act(() => useActiveLocationStore.getState().setLocationName(51.5074, -0.1278, 'London'))
    const { container } = renderBadge()
    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing for a confirmed place whose level is unknown', () => {
    act(() => useActiveLocationStore.getState().setFromAgent(25.1558, 55.2218, 'Al Safa Park 2', 0.9))
    const { container } = renderBadge()
    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing when no boundary is active', () => {
    const { container } = render(<SiteBoundaryConfidenceBadge />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing when a site name is set but no confidence level', () => {
    act(() => useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, confidenceLevel: null }))
    const { container } = renderBadge()
    expect(container).toBeEmptyDOMElement()
  })

  // specs/073 contract B2/B4 (FR-006, FR-007, FR-012) — the label and the name, no provenance.
  it('shows exactly the site name and the confidence label, never the source or alternatives', () => {
    act(() =>
      useActiveSiteBoundaryStore.getState().setBoundary({
        ...sampleBoundary,
        alternativeCandidateNames: ['Al Safa Park (Landuse)'],
      }),
    )
    renderBadge()

    const badge = screen.getByRole('status')
    expect(badge).toHaveAccessibleName('Al Safa Park 2 boundary: High confidence')
    expect(badge).toHaveTextContent(/^High confidenceAl Safa Park 2$/)

    // One line, left to right: shield, confidence label, divider, site name.
    const icon = badge.querySelector('svg')
    const divider = badge.querySelector('.MuiDivider-root')
    expect(divider).toHaveAttribute('aria-hidden', 'true')
    expect(divider).toHaveClass('MuiDivider-vertical')
    const order = [icon!, screen.getByText('High confidence'), divider!, screen.getByText('Al Safa Park 2')]
    for (let i = 1; i < order.length; i++) {
      expect(order[i - 1].compareDocumentPosition(order[i]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    }
    expect(badge).not.toHaveTextContent(/Source:/)
    expect(badge).not.toHaveTextContent(/Also considered:/)
    expect(badge).not.toHaveTextContent(/OpenStreetMap/)
    expect(badge).not.toHaveTextContent(/Landuse/)
  })

  it('truncates a long site name to one line rather than wrapping (B2, B5)', () => {
    const longName = 'Mohammed Bin Rashid Al Maktoum City — District One Crystal Lagoon Park'
    act(() => useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, siteName: longName }))
    renderBadge()

    const name = screen.getByText(longName)
    expect(window.getComputedStyle(name).whiteSpace).toBe('nowrap')
    expect(window.getComputedStyle(name).textOverflow).toBe('ellipsis')
    expect(window.getComputedStyle(screen.getByRole('status')).maxWidth).toBe('360px')
  })

  it.each(['light', 'dark'] as const)('uses the shared 40 px HUD surface with no violet accent (%s)', (mode) => {
    act(() => useActiveSiteBoundaryStore.getState().setBoundary(sampleBoundary))
    const { container } = renderBadge(mode)

    const styles = renderedStyles(container).toUpperCase()
    expect(styles).not.toContain('9C62DE')
    expect(styles).not.toContain('156, 98, 222') // #9C62DE as an rgb()/rgba() triple
    expect(window.getComputedStyle(screen.getByRole('status')).height).toBe('40px')
  })

  // FR-006/WCAG 2.1 AA — confidence must be distinguishable by text/icon, not color alone.
  it.each([
    ['low', 'Low confidence — approximate'],
    ['medium', 'Medium confidence'],
    ['high', 'High confidence'],
  ] as const)('states the %s confidence level as text, not just a color', (level, expectedLabel) => {
    act(() => useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, confidenceLevel: level }))
    renderBadge()

    expect(screen.getByText(expectedLabel)).toBeInTheDocument()
  })

  // specs/073 US3 (FR-008–FR-011, SC-004) — the shield's shape and colour both follow the level,
  // and the text label stays, so colour is never the only signal. axe does not check non-text
  // contrast, so the 3:1 floor is asserted here directly.
  describe.each(['light', 'dark'] as const)('confidence shield (%s theme)', (mode) => {
    const theme = createAppTheme(mode)
    const shade = mode === 'dark' ? 'light' : 'main'

    it.each([
      ['high', 'GppGoodOutlinedIcon', theme.palette.success[shade], 'High confidence'],
      ['medium', 'ShieldOutlinedIcon', theme.palette.warning[shade], 'Medium confidence'],
      ['low', 'GppMaybeOutlinedIcon', theme.palette.error[shade], 'Low confidence — approximate'],
    ] as const)('%s: %s in the palette colour, ≥3:1 against the card, with its label', (level, testId, colour, label) => {
      act(() => useActiveSiteBoundaryStore.getState().setBoundary({ ...sampleBoundary, confidenceLevel: level }))
      renderBadge(mode)

      const icon = screen.getByTestId(testId)
      expect(icon).toHaveAttribute('aria-hidden', 'true')
      expect(window.getComputedStyle(icon).color).toBe(computedColour(colour))
      expect(getContrastRatio(colour, theme.palette.background.paper)).toBeGreaterThanOrEqual(3)
      expect(screen.getByText(label)).toBeInTheDocument()
    })
  })
})
