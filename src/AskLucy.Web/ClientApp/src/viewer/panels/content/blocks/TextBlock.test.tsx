import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { TextBlockRenderer } from './TextBlock'

describe('TextBlockRenderer', () => {
  it('renders the text content', () => {
    render(<TextBlockRenderer block={{ kind: 'text', text: 'Resolved from the site boundary service.' }} />)
    expect(screen.getByText('Resolved from the site boundary service.')).toBeInTheDocument()
  })

  it('preserves line breaks', () => {
    const { container } = render(<TextBlockRenderer block={{ kind: 'text', text: 'Line one\nLine two' }} />)
    const el = container.querySelector('p')!
    expect(el.textContent).toBe('Line one\nLine two')
    expect(getComputedStyle(el).whiteSpace).toBe('pre-wrap')
  })

  it('renders markup-like text literally, never as HTML (spec FR-005, constitution §8)', () => {
    const dangerous = '<script>window.__pwned=true</script>'
    const { container } = render(<TextBlockRenderer block={{ kind: 'text', text: dangerous }} />)
    expect(screen.getByText(dangerous)).toBeInTheDocument()
    expect(container.querySelectorAll('script')).toHaveLength(0)
  })
})
