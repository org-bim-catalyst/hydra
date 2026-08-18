import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import { SkeletonBlock } from './SkeletonBlock'

expect.extend(toHaveNoViolations)

describe('SkeletonBlock accessibility (FR-004, FR-008)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(<SkeletonBlock variant="card" count={3} />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
