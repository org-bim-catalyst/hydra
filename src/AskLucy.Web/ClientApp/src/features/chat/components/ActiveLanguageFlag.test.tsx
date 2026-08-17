import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ActiveLanguageFlag } from './ActiveLanguageFlag'

describe('ActiveLanguageFlag', () => {
  it.each([
    ['en', 'English'],
    ['ar', 'Arabic'],
    ['es', 'Spanish'],
    ['fr', 'French'],
    ['de', 'German'],
  ])('renders the flag for %s (%s)', (code, label) => {
    render(<ActiveLanguageFlag language={code} />)
    expect(screen.getByRole('img', { name: `Response language: ${label}` })).toBeInTheDocument()
  })

  it('falls back to a default glyph for an unrecognized code (Edge Cases)', () => {
    render(<ActiveLanguageFlag language="zz" />)
    expect(
      screen.getByRole('img', { name: 'Response language: Default language' }),
    ).toBeInTheDocument()
  })
})
