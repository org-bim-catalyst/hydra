import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import { UploadPanel } from './UploadPanel'

expect.extend(toHaveNoViolations)

describe('UploadPanel accessibility (FR-001–FR-004, FR-006, FR-007, FR-052)', () => {
  it('has no automatically detectable a11y violations in its idle (empty queue) state', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByLabelText } = render(
      <QueryClientProvider client={queryClient}>
        <UploadPanel />
      </QueryClientProvider>,
    )

    await findByLabelText('Upload documents')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
