import { ThemeProvider } from '@mui/material'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { createAppTheme } from '../../../theme'
import { ForgotPasswordPage } from './ForgotPasswordPage'

expect.extend(toHaveNoViolations)

function renderPage(mode: 'light' | 'dark') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <ThemeProvider theme={createAppTheme(mode)}>
          <ForgotPasswordPage />
        </ThemeProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('ForgotPasswordPage accessibility (specs/058-password-recovery FR-018)', () => {
  it.each(['light', 'dark'] as const)('has no automatically detectable a11y violations in %s theme', async (mode) => {
    const { container } = renderPage(mode)

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
