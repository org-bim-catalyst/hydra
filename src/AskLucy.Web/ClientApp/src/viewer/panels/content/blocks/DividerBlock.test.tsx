import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DividerBlockRenderer } from './DividerBlock'

describe('DividerBlockRenderer', () => {
  it('renders a horizontal rule', () => {
    const { container } = render(<DividerBlockRenderer />)
    expect(container.querySelector('hr')).toBeInTheDocument()
  })
})
