import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { RegisterPage } from './RegisterPage'

expect.extend(toHaveNoViolations)

function renderRegisterPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <RegisterPage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('RegisterPage accessibility (spec 010-lucy-brand-refresh FR-012/FR-017)', () => {
  it('has no automatically detectable a11y violations after the redesign', async () => {
    const { container } = renderRegisterPage()

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it("renders Lucy's portrait with the same treatment as the login page (FR-012/FR-013)", () => {
    renderRegisterPage()

    expect(screen.getByRole('img', { name: 'Lucy' })).toBeInTheDocument()
  })
})
