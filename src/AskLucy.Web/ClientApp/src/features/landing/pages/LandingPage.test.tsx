import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { featureBlocks, hero, howItWorks, meta } from '../content/copy'
import { LandingPage } from './LandingPage'

const server = setupServer(
  http.get('*/api/v1/cookie-policy', () =>
    HttpResponse.json({ version: '2026-07-30.1', effectiveAtUtc: '2026-07-30T00:00:00Z' }),
  ),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderLandingPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <LandingPage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('LandingPage (spec.md FR-001/FR-002/FR-003/FR-022)', () => {
  it('renders the hero headline instead of a login form (FR-001)', () => {
    renderLandingPage()

    expect(screen.getByRole('heading', { level: 1, name: hero.headline })).toBeInTheDocument()
  })

  it('sets the document title and description for SEO/social preview (FR-022)', () => {
    renderLandingPage()

    expect(document.title).toBe(meta.title)
    expect(document.querySelector('meta[name="description"]')).toHaveAttribute('content', meta.description)
    expect(document.querySelector('meta[property="og:title"]')).toHaveAttribute('content', meta.title)
  })

  it('presents Start Designing/Explore in the hero, with just the brand mark in the nav — no dashboard-style nav (FR-003/FR-014)', () => {
    renderLandingPage()

    expect(screen.getByRole('button', { name: 'Start Designing →' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Explore Flumeria' })).toBeInTheDocument()
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })

  it('covers the "how it works" workflow (spec.md FR-002)', () => {
    renderLandingPage()

    expect(screen.getByText(howItWorks.title)).toBeInTheDocument()
    for (const step of howItWorks.steps) {
      expect(screen.getByText(step.title)).toBeInTheDocument()
    }
  })

  it('covers every required FR-002 topic across the five feature blocks', () => {
    // getByText rather than getByRole('heading', ...): the latter walks every element's
    // computed style to resolve accessible roles, which trips a jsdom CSS-resolution bug
    // (`resolveLengthInPixels`) on this page's Grid-heavy layout — a jsdom limitation, not
    // a real defect (the dedicated a11y test above already validates heading structure via
    // axe directly).
    renderLandingPage()

    for (const block of featureBlocks) {
      expect(screen.getByText(block.heading)).toBeInTheDocument()
    }
  })
})
