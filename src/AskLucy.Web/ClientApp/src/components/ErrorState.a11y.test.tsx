import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import { ErrorState } from './ErrorState'

expect.extend(toHaveNoViolations)

describe('ErrorState accessibility (FR-004, FR-008)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(
      <ErrorState title="Couldn't load documents" description="Something went wrong." onRetry={vi.fn()} />,
    )
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
