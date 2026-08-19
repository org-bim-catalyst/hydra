import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import { CircularAction } from './CircularAction'

expect.extend(toHaveNoViolations)

describe('CircularAction accessibility (FR-019)', () => {
  it('has no automatically detectable a11y violations when collapsed', async () => {
    const { container } = render(
      <CircularAction id="layers" label="Layers" icon={<span aria-hidden="true">L</span>} expanded={false} onToggle={() => {}}>
        <button type="button">Layer action</button>
      </CircularAction>,
    )
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations when expanded', async () => {
    const { container } = render(
      <CircularAction id="layers" label="Layers" icon={<span aria-hidden="true">L</span>} expanded onToggle={() => {}}>
        <button type="button">Layer action</button>
      </CircularAction>,
    )
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
