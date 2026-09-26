import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { describe, expect, it } from 'vitest'
import { AI_VOICE_DISCLOSURE } from '../../chat/voice/aiVoiceDisclosure'
import { LandingFooter } from '../../landing/components/LandingFooter'
import { TermsPage } from './TermsPage'

expect.extend(toHaveNoViolations)

function renderTerms() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <TermsPage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

/** The landing page, whose footer's real Terms link sets the navigation state under test. */
function renderLandingWithTerms() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  function Landing() {
    const { state } = useLocation()
    return (
      <>
        <p>Landing page{(state as { viewLanding?: boolean } | null)?.viewLanding ? ' (kept)' : ''}</p>
        <LandingFooter />
      </>
    )
  }
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <Routes>
          <Route path="/" element={<Landing />} />
          <Route path="/terms" element={<TermsPage />} />
        </Routes>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('TermsPage', () => {
  it('binds every user to all thirteen OpenRAIL-M use restrictions', () => {
    renderTerms()

    const restrictions = within(screen.getByText(/You agree not to/).parentElement!).getAllByRole('listitem')
    expect(restrictions).toHaveLength(13)
    expect(restrictions[4]).toHaveTextContent(/without expressly and intelligibly disclaiming that it is machine generated/)
    expect(restrictions[11]).toHaveTextContent(/medical advice/)
  })

  it('discloses that Lucy speaks with an AI-generated voice', () => {
    renderTerms()
    expect(screen.getByText(new RegExp(AI_VOICE_DISCLOSURE.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))).toBeInTheDocument()
  })

  it("links to the voice model's licence and to the Privacy Policy", () => {
    renderTerms()

    expect(screen.getByRole('link', { name: "model's Hugging Face page" })).toHaveAttribute(
      'href',
      'https://huggingface.co/Supertone/supertonic-3',
    )
    expect(screen.getAllByRole('link', { name: /Privacy Policy/ })[0]).toHaveAttribute('href', '/privacy')
  })

  it('credits the map, building-outline and building-height sources', () => {
    renderTerms()

    const section = screen.getByRole('heading', { name: '8. Map and building data' }).parentElement!
    expect(section).toHaveTextContent(/© Google/)
    expect(section).toHaveTextContent(/Overture Maps Foundation/)
    expect(section).toHaveTextContent(/source: Esri, Vantor/)
    expect(within(section).getByRole('link', { name: '© OpenStreetMap contributors' })).toHaveAttribute(
      'href',
      'https://www.openstreetmap.org/copyright',
    )
    expect(within(section).getByRole('link', { name: 'Open Database License' })).toHaveAttribute(
      'href',
      'https://opendatacommons.org/licenses/odbl/',
    )
  })

  it('returns to the landing page when opened from it', async () => {
    const user = userEvent.setup()
    renderLandingWithTerms()

    await user.click(screen.getByRole('link', { name: 'Terms' }))
    await user.click(await screen.findByRole('link', { name: 'Ask Lucy home' }))

    // `VIEW_LANDING_STATE` rides along, so a signed-in visitor isn't bounced on to the Studio.
    expect(await screen.findByText('Landing page (kept)')).toBeInTheDocument()
  })

  it('returns to the Studio when opened from anywhere else', () => {
    renderTerms()
    expect(screen.getByRole('link', { name: 'Ask Lucy home' })).toHaveAttribute('href', '/studio')
  })

  it('has no automatically detectable a11y violations when reached without authentication', async () => {
    const { container } = renderTerms()
    expect(await axe(container)).toHaveNoViolations()
  })
})
