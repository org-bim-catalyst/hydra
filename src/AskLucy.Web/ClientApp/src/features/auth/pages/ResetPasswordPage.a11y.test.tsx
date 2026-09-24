import { ThemeProvider } from '@mui/material'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { createAppTheme } from '../../../theme'
import { ResetPasswordPage } from './ResetPasswordPage'

expect.extend(toHaveNoViolations)

// The page now checks the link before rendering the form, so without this the audit would only
// ever see the loading spinner.
const server = setupServer(
  http.get('*/api/v1/cookie-policy', () =>
    HttpResponse.json({ version: '2026-07-30.1', effectiveAtUtc: '2026-07-30T00:00:00Z' }),
  ),
  http.post('*/api/v1/auth/password/reset/validate', () => new HttpResponse(null, { status: 204 })),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

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
    await screen.findByLabelText('New password')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
