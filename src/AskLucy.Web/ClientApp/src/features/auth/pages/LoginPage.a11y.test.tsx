import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { LoginPage } from './LoginPage'

expect.extend(toHaveNoViolations)

function renderLoginPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <LoginPage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('LoginPage accessibility (spec 010-lucy-brand-refresh FR-011/FR-017)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container } = renderLoginPage()

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it("renders Lucy's portrait with meaningful alt text as part of the page branding (FR-011/FR-013)", () => {
    renderLoginPage()

    expect(screen.getByRole('img', { name: 'Lucy' })).toBeInTheDocument()
  })
})
