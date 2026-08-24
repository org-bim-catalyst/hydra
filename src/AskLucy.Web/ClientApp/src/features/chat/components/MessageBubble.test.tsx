import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
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

  it('renders a provider/model attribution chip when present (specs/005-multi-provider-ai-engine FR-011)', () => {
    render(
      <MessageBubble
        message={{ role: 'assistant', content: 'Hello', provider: 'OpenAI', model: 'gpt-4' }}
      />,
    )
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
          citations: [
            { id: 'c1', sourceLabel: 'Handbook', sourceReference: 'https://example.com/handbook' },
          ],
        }}
      />,
    )
    expect(screen.getByText('Handbook')).toBeInTheDocument()
  })
})

// specs/039-composer-interaction-states-redesign FR-020–FR-025 (User Story 5) — the
// replay/stop control in the reply's lower-right corner. showStopIcon/isReplayDisabled are
// always supplied by the caller (ChatPage.tsx) in real usage; these tests exercise the
// component's own rendering/click-dispatch logic in isolation.
describe('MessageBubble — replay control (US5, FR-020–FR-025)', () => {
  it('renders no replay control on a user message, even with replay props supplied', () => {
    render(
      <MessageBubble
        message={{ role: 'user', content: 'Hi', id: 'm1' }}
        showStopIcon={false}
        isReplayDisabled={false}
        onReplay={vi.fn()}
        onStopReplay={vi.fn()}
      />,
    )
    expect(screen.queryByRole('button', { name: /replay/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /stop/i })).not.toBeInTheDocument()
  })

  it('renders no replay control on an assistant message with no stable id yet (still streaming — research.md Decision 7)', () => {
    render(
      <MessageBubble
        message={{ role: 'assistant', content: 'Thinking' }}
        showStopIcon={false}
        isReplayDisabled={false}
        onReplay={vi.fn()}
        onStopReplay={vi.fn()}
      />,
    )
    expect(screen.queryByRole('button', { name: /replay/i })).not.toBeInTheDocument()
  })

  it('renders no replay control at all when the caller does not wire onReplay (e.g. isolated content-only tests)', () => {
    render(<MessageBubble message={{ role: 'assistant', content: 'Hello', id: 'm1' }} />)
    expect(screen.queryByRole('button', { name: /replay/i })).not.toBeInTheDocument()
  })

  it('shows an enabled Replay (play) control when not disabled and not currently playing', () => {
    render(
      <MessageBubble
        message={{ role: 'assistant', content: 'Hello', id: 'm1' }}
        showStopIcon={false}
        isReplayDisabled={false}
        onReplay={vi.fn()}
        onStopReplay={vi.fn()}
      />,
    )
    const button = screen.getByRole('button', { name: /replay/i })
    expect(button).not.toBeDisabled()
  })

  it('disables the Replay control when isReplayDisabled is true', () => {
    render(
      <MessageBubble
        message={{ role: 'assistant', content: 'Hello', id: 'm1' }}
        showStopIcon={false}
        isReplayDisabled={true}
        onReplay={vi.fn()}
        onStopReplay={vi.fn()}
      />,
    )
    expect(screen.getByRole('button', { name: /replay/i })).toBeDisabled()
  })

  it('clicking Replay calls onReplay with the message', () => {
    const onReplay = vi.fn()
    const message = { role: 'assistant' as const, content: 'Hello', id: 'm1' }
    render(
      <MessageBubble
        message={message}
        showStopIcon={false}
        isReplayDisabled={false}
        onReplay={onReplay}
        onStopReplay={vi.fn()}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: /replay/i }))
    expect(onReplay).toHaveBeenCalledWith(message)
  })

  it('shows an always-enabled Stop control when showStopIcon is true, even if isReplayDisabled is also true', () => {
    render(
      <MessageBubble
        message={{ role: 'assistant', content: 'Hello', id: 'm1' }}
        showStopIcon={true}
        isReplayDisabled={true}
        onReplay={vi.fn()}
        onStopReplay={vi.fn()}
      />,
    )
    const button = screen.getByRole('button', { name: /stop/i })
    expect(button).not.toBeDisabled()
    expect(screen.queryByRole('button', { name: /replay/i })).not.toBeInTheDocument()
  })

  it('clicking Stop calls onStopReplay, not onReplay', () => {
    const onReplay = vi.fn()
    const onStopReplay = vi.fn()
    render(
      <MessageBubble
        message={{ role: 'assistant', content: 'Hello', id: 'm1' }}
        showStopIcon={true}
        isReplayDisabled={false}
        onReplay={onReplay}
        onStopReplay={onStopReplay}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: /stop/i }))
    expect(onStopReplay).toHaveBeenCalledTimes(1)
    expect(onReplay).not.toHaveBeenCalled()
  })
})
