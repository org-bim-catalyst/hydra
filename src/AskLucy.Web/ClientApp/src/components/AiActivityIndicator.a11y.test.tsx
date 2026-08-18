import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import { AiActivityIndicator } from './AiActivityIndicator'

expect.extend(toHaveNoViolations)

describe('AiActivityIndicator accessibility (FR-004, FR-007)', () => {
  it('exposes each state via an accessible status role', () => {
    render(<AiActivityIndicator state="tool-executing" label="Analyzing document…" />)
    expect(screen.getByRole('status', { name: 'Analyzing document…' })).toBeInTheDocument()
  })

  it('has no automatically detectable a11y violations for every state', async () => {
    for (const state of ['thinking', 'streaming', 'tool-executing'] as const) {
      const { container, unmount } = render(<AiActivityIndicator state={state} />)
      const results = await axe(container)
      expect(results).toHaveNoViolations()
      unmount()
    }
  })
})
