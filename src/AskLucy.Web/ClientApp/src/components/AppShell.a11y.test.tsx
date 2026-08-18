import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { AppShell } from './AppShell'

expect.extend(toHaveNoViolations)

describe('AppShell accessibility (FR-004, research.md #1)', () => {
  it('has no automatically detectable a11y violations with a page title and actions', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByRole } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AppShell title="Example page" subtitle="A subtitle" actions={<button>Action</button>}>
            <div>Page content</div>
          </AppShell>
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByRole('link', { name: 'Ask Lucy home' })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with no title/actions (chrome-only usage)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByRole } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AppShell>
            <div>Page content</div>
          </AppShell>
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByRole('link', { name: 'Ask Lucy home' })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
