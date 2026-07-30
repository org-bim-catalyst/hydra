import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import { ThinkingIndicator } from './ThinkingIndicator'

expect.extend(toHaveNoViolations)

describe('ThinkingIndicator', () => {
  it('renders three animated dots with role=status', () => {
    render(<ThinkingIndicator />)
    const indicator = screen.getByRole('status', { name: 'Ask Lucy is thinking' })
    expect(indicator.children).toHaveLength(3)
  })

  it('has no automatically detectable a11y violations (constitution §7, §10)', async () => {
    const { container } = render(<ThinkingIndicator />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
