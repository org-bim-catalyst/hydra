import { ThemeProvider } from '@mui/material'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { createAppTheme } from '../../../theme'
import { ResetPasswordPage } from './ResetPasswordPage'

expect.extend(toHaveNoViolations)

function renderPage(mode: 'light' | 'dark') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={['/reset-password?userId=user-1&token=AABBCC']}>
      <QueryClientProvider client={queryClient}>
        <ThemeProvider theme={createAppTheme(mode)}>
          <ResetPasswordPage />
        </ThemeProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('ResetPasswordPage accessibility (specs/058-password-recovery FR-018, US2)', () => {
  it.each(['light', 'dark'] as const)('has no automatically detectable a11y violations in %s theme', async (mode) => {
    const { container } = renderPage(mode)

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
