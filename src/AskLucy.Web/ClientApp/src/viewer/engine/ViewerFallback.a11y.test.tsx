import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import { ViewerFallback } from './ViewerFallback'

expect.extend(toHaveNoViolations)

describe('ViewerFallback accessibility (FR-005)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(<ViewerFallback />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('is aria-hidden and contains no focusable/interactive elements', () => {
    const { container } = render(<ViewerFallback />)
    const root = container.firstElementChild
    expect(root).toHaveAttribute('aria-hidden', 'true')
    expect(container.querySelectorAll('button, a, input, [tabindex]')).toHaveLength(0)
  })
})
