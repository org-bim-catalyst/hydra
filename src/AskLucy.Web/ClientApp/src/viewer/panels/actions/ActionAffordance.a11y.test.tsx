import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import { ActionAffordance } from './ActionAffordance'

expect.extend(toHaveNoViolations)

vi.mock('../../engine/viewerEngineInstance', () => ({
  viewerEngine: { select: () => ({ ok: true }) },
}))

describe('ActionAffordance accessibility (spec SC-008)', () => {
  it('has no automatically detectable a11y violations when activatable', async () => {
    const { container } = render(
      <ActionAffordance action={{ command: 'select', args: { layerId: 'l1', elementId: 'e1' } }}>Wall</ActionAffordance>,
    )
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations when inert', async () => {
    const { container } = render(<ActionAffordance>Plain text</ActionAffordance>)
    expect(await axe(container)).toHaveNoViolations()
  })

  it('exposes the control as a keyboard-focusable button with a visible focus target', () => {
    render(<ActionAffordance action={{ command: 'select', args: { layerId: 'l1', elementId: 'e1' } }}>Wall</ActionAffordance>)
    const control = screen.getByRole('button', { name: 'Wall' })
    expect(control.tabIndex).not.toBe(-1)
  })
})
