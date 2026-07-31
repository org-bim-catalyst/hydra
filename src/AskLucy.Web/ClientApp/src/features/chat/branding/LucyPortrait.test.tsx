import { render, screen } from '@testing-library/react'
import { fireEvent } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { LucyPortrait } from './LucyPortrait'

describe('LucyPortrait', () => {
  it('renders the portrait image with the supplied alt text', () => {
    render(<LucyPortrait variant="toggle" alt="Lucy" />)

    const img = screen.getByRole('img', { name: 'Lucy' })
    expect(img).toBeInTheDocument()
    expect(img.tagName).toBe('IMG')
  })

  it('sizes the toggle and auth variants differently', () => {
    const { rerender } = render(<LucyPortrait variant="toggle" alt="Lucy" />)
    const toggleClassName = screen.getByRole('img', { name: 'Lucy' }).className

    rerender(<LucyPortrait variant="auth" alt="Ask Lucy" />)
    const authClassName = screen.getByRole('img', { name: 'Ask Lucy' }).className

    // MUI/emotion generates a distinct utility class per distinct `sx` (size) value.
    expect(authClassName).not.toBe(toggleClassName)
  })

  it('falls back to a generic avatar icon rather than a broken image when the asset fails to load (FR-014)', () => {
    render(<LucyPortrait variant="auth" alt="Ask Lucy" />)

    const img = screen.getByRole('img', { name: 'Ask Lucy' })
    fireEvent.error(img)

    expect(screen.queryByRole('img', { name: 'Ask Lucy' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Ask Lucy')).toBeInTheDocument()
  })
})
