import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { PromptEditor } from './PromptEditor'

expect.extend(toHaveNoViolations)

describe('PromptEditor accessibility (spec.md "Prompt Editor" UI requirements)', () => {
  it('has no automatically detectable a11y violations in create mode (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByLabelText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <PromptEditor />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByLabelText('Name', { exact: false })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
