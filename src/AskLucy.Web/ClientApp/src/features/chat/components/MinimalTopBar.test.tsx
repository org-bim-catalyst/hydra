import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { MinimalTopBar } from './MinimalTopBar'

function renderMinimalTopBar() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <MinimalTopBar />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('MinimalTopBar brand-transition element (spec.md FR-011, US3 Scenario 4)', () => {
  it('shows the Flumeria identity only, with no other change to the bar', () => {
    renderMinimalTopBar()

    expect(screen.getByText('Flumeria')).toBeInTheDocument()
    expect(screen.queryByText('Ask Lucy')).not.toBeInTheDocument()
    // Still only the brand cluster, theme toggle, and account menu — no new nav items.
    expect(screen.getByLabelText('Toggle theme')).toBeInTheDocument()
    expect(screen.getByLabelText('Account menu')).toBeInTheDocument()
  })
})
