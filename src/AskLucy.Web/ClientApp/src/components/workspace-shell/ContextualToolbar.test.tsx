import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ContextualToolbar } from './ContextualToolbar'

describe('ContextualToolbar', () => {
  it('renders its children (delegates layout to FloatingToolbar)', () => {
    render(
      <ContextualToolbar anchor="top-end">
        <button type="button">Analyze</button>
      </ContextualToolbar>,
    )
    expect(screen.getByRole('button', { name: 'Analyze' })).toBeInTheDocument()
  })

  it('renders nothing extra when given zero children', () => {
    const { container } = render(<ContextualToolbar anchor="top-end">{null}</ContextualToolbar>)
    expect(container.firstElementChild).toBeInTheDocument()
    expect(container.firstElementChild?.children.length).toBe(0)
  })
})
