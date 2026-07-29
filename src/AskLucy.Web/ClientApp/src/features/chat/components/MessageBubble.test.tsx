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

  it('renders provider/model metadata for assistant messages', () => {
    render(<MessageBubble message={{ role: 'assistant', content: 'Hello', provider: 'OpenAI', model: 'gpt-4' }} />)
    expect(screen.getByText('OpenAI · gpt-4')).toBeInTheDocument()
  })

  it('does not render metadata caption when absent', () => {
    render(<MessageBubble message={{ role: 'assistant', content: 'Hello' }} />)
    expect(screen.queryByText(/·/)).not.toBeInTheDocument()
  })

  it('renders an attachment chip linking to its access location', () => {
    render(
      <MessageBubble
        message={{
          role: 'assistant',
          content: 'See attached',
          attachments: [{ id: 'a1', fileName: 'report.pdf', accessLocation: '/files/report.pdf' }],
        }}
      />,
    )
    const link = screen.getByText('report.pdf').closest('a')
    expect(link).toHaveAttribute('href', '/files/report.pdf')
  })

  it('renders a citation chip', () => {
    render(
      <MessageBubble
        message={{
          role: 'assistant',
          content: 'Per the source',
          citations: [{ id: 'c1', sourceLabel: 'Handbook', sourceReference: 'https://example.com/handbook' }],
        }}
      />,
    )
    expect(screen.getByText('Handbook')).toBeInTheDocument()
  })
})
