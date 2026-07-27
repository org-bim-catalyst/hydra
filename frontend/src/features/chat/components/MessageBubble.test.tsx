import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { MessageBubble } from './MessageBubble'

describe('MessageBubble', () => {
  it('renders Markdown content', () => {
    render(<MessageBubble message={{ role: 'assistant', content: '**bold** text' }} />)
    expect(screen.getByText('bold')).toBeInTheDocument()
  })

  it('renders KaTeX math expressions', () => {
    render(<MessageBubble message={{ role: 'assistant', content: '$E = mc^2$' }} />)
    expect(document.querySelector('.katex')).not.toBeNull()
  })
})
