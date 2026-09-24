import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { AI_VOICE_DISCLOSURE } from '../../chat/voice/aiVoiceDisclosure'
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

  it('has no automatically detectable a11y violations when reached without authentication', async () => {
    const { container } = renderTerms()
    expect(await axe(container)).toHaveNoViolations()
  })
})
