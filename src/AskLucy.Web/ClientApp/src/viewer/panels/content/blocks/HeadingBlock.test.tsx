import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { HeadingBlockRenderer } from './HeadingBlock'

describe('HeadingBlockRenderer', () => {
  it('renders the heading text', () => {
    render(<HeadingBlockRenderer block={{ kind: 'heading', text: 'Al Safa Park 2' }} />)
    expect(screen.getByText('Al Safa Park 2')).toBeInTheDocument()
  })

  it('renders markup-like text literally, never as HTML (spec FR-005, constitution §8)', () => {
    const dangerous = '<img src=x onerror="window.__pwned=true">'
    render(<HeadingBlockRenderer block={{ kind: 'heading', text: dangerous }} />)
    expect(screen.getByText(dangerous)).toBeInTheDocument()
    expect(document.querySelector('img')).toBeNull()
  })

  it('renders a distinct, smaller variant for level 2 than the level-1 default', () => {
    render(<HeadingBlockRenderer block={{ kind: 'heading', text: 'Sub', level: 2 }} />)
    expect(screen.getByText('Sub').className).toContain('MuiTypography-subtitle1')

    render(<HeadingBlockRenderer block={{ kind: 'heading', text: 'Main' }} />)
    expect(screen.getByText('Main').className).toContain('MuiTypography-h6')
  })
})
