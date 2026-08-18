import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import { EmptyState } from './EmptyState'

expect.extend(toHaveNoViolations)

describe('EmptyState accessibility (FR-004, FR-008)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(
      <EmptyState
        title="No knowledge bases yet"
        description="Create one to start grounding chats in your own documents."
      />,
    )
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
