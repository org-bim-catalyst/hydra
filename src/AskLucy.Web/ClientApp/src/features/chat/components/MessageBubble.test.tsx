import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ActionAttempt, TurnOutcome } from '../api/aiApi'
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

  it('never renders provider/model attribution text, even when present on the message (specs/003-chat-loading-ux-fixes SC-004)', () => {
    render(
      <MessageBubble
        message={{ role: 'assistant', content: 'Hello', provider: 'OpenAI', model: 'gpt-4' }}
      />,
    )
    expect(screen.queryByText('OpenAI · gpt-4')).not.toBeInTheDocument()
    expect(screen.queryByText(/OpenAI/)).not.toBeInTheDocument()
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

  it('renders Replay and Copy together in one row when onReplay is provided', () => {
    render(
      <MessageBubble
        message={{ role: 'assistant', content: 'Hello', id: 'm1' }}
        showStopIcon={false}
        isReplayDisabled={false}
        onReplay={vi.fn()}
        onStopReplay={vi.fn()}
      />,
    )
    expect(screen.getByRole('button', { name: /replay/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /copy/i })).toBeInTheDocument()
  })
})

// specs/046-reply-action-bar FR-001–FR-003, FR-009 — the Copy action, independent of whether
// replay is wired up by the caller.
describe('MessageBubble — copy control (specs/046-reply-action-bar)', () => {
  const originalClipboard = navigator.clipboard

  afterEach(() => {
    Object.defineProperty(navigator, 'clipboard', { value: originalClipboard, configurable: true })
  })

  it('renders no Copy action on a user message', () => {
    render(<MessageBubble message={{ role: 'user', content: 'Hi', id: 'm1' }} />)
    expect(screen.queryByRole('button', { name: /copy/i })).not.toBeInTheDocument()
  })

  it('renders no Copy action on an assistant message with no stable id yet (still streaming)', () => {
    render(<MessageBubble message={{ role: 'assistant', content: 'Thinking' }} />)
    expect(screen.queryByRole('button', { name: /copy/i })).not.toBeInTheDocument()
  })

  it('renders Copy even when the caller does not wire replay at all (FR-009)', () => {
    render(<MessageBubble message={{ role: 'assistant', content: 'Hello', id: 'm1' }} />)
    expect(screen.getByRole('button', { name: /copy/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /replay/i })).not.toBeInTheDocument()
  })

  it('copies the message text and shows a visible success confirmation', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    render(<MessageBubble message={{ role: 'assistant', content: 'Copy me', id: 'm1' }} />)

    fireEvent.click(screen.getByRole('button', { name: /copy/i }))
    expect(writeText).toHaveBeenCalledWith('Copy me')

    await screen.findByRole('button', { name: /copied/i })
  })

  it('shows a visible failure indication when the clipboard write is rejected, never a silent no-op', async () => {
    const writeText = vi.fn().mockRejectedValue(new Error('permission denied'))
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    render(<MessageBubble message={{ role: 'assistant', content: 'Copy me', id: 'm1' }} />)

    fireEvent.click(screen.getByRole('button', { name: /copy/i }))

    await screen.findByRole('button', { name: /copy failed/i })
  })
})

/**
 * specs/068 T057 (US2, FR-013/FR-013b/FR-014) - the retry affordance.
 *
 * The rules under test are about *when it may appear*, not about what the server does with it: a
 * control offered on a turn that did not fail invites the user to re-run something that worked, and
 * FR-015 exists precisely because that is a thing the system must refuse to do. The cheapest place
 * to keep that promise is here, where the control is decided.
 */
describe('MessageBubble retry control (specs/068 US2)', () => {
  const failed: ActionAttempt = {
    kind: 'Capability',
    key: 'resolve_location',
    targetLabel: 'Al Safa Park 2',
    succeeded: false,
    failureReason: 'the provider was unavailable',
  }
  const succeeded: ActionAttempt = { ...failed, succeeded: true, failureReason: null }

  const outcome = (attempts: ActionAttempt[]): TurnOutcome => ({
    verdict: attempts.some((a) => a.succeeded) ? 'Acted' : 'FailedBeforeCompleting',
    attempts,
    failureReason: null,
    recordedAtUtc: '2026-09-24T10:00:00Z',
  })

  const renderBubble = (
    props: Partial<Parameters<typeof MessageBubble>[0]> = {},
    attempts: ActionAttempt[] = [failed],
  ) =>
    render(
      <MessageBubble
        message={{
          id: 'msg-1',
          role: 'assistant',
          content: "Something went wrong partway through and I couldn't finish.",
          turnOutcome: outcome(attempts),
        }}
        onRetry={vi.fn().mockResolvedValue(undefined)}
        {...props}
      />,
    )

  it('offers a retry on a turn that recorded a failed attempt', () => {
    renderBubble()
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })

  it('offers a retry on a partly-failed turn, where the words alone can look like success', () => {
    // FR-006 - the verdict is "Acted"; one attempt still failed, and that is the half the user has
    // no other way to recover from.
    renderBubble({}, [succeeded, { ...failed, key: 'apply_boundary' }])
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })

  it('offers no retry when every recorded attempt succeeded', () => {
    // FR-015 - nothing here may invite re-running something that already worked.
    renderBubble({}, [succeeded])
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
  })

  it('offers no retry when the turn recorded no outcome at all', () => {
    render(
      <MessageBubble
        message={{ id: 'msg-1', role: 'assistant', content: 'Hello' }}
        onRetry={vi.fn()}
      />,
    )
    // An absent outcome (a message written before specs/068) is unknown, never a failure.
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
  })

  it('offers no retry when the caller never wired one', () => {
    renderBubble({ onRetry: undefined })
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
  })

  it('asks the server to retry by message id alone', async () => {
    const onRetry = vi.fn().mockResolvedValue(undefined)
    renderBubble({ onRetry })

    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))

    // FR-010 - the id is the whole request; the capability and its arguments are the server's own
    // record. Nothing about the failed action is read off the rendered message.
    await waitFor(() => expect(onRetry).toHaveBeenCalledWith('msg-1'))
    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('disables the retry while a dispatched turn is already running', () => {
    renderBubble({ isSubmittingAction: true })
    expect(screen.getByRole('button', { name: 'Try again' })).toBeDisabled()
  })

  it('surfaces a rejected retry as visible text, not a console line', async () => {
    const onRetry = vi.fn().mockRejectedValue(new Error('The chat could not be created.'))
    renderBubble({ onRetry })

    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))

    // FR-014 / CLAUDE.md Error Handling - the failure reaches the user where they pressed.
    expect(await screen.findByText('The chat could not be created.')).toBeInTheDocument()
  })

  it('surfaces a retry failure the hook caught on its behalf', async () => {
    // `retryAction` swallows its own stream errors into `actionError` rather than rejecting, so a
    // rejection is not the only way this can fail - both routes have to end up on screen.
    const onRetry = vi.fn().mockResolvedValue(undefined)
    renderBubble({ onRetry, actionError: 'Choose an AI provider and model before sending a message.' })

    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(
      await screen.findByText('Choose an AI provider and model before sending a message.'),
    ).toBeInTheDocument()
  })

  it('stays quiet about an error belonging to some other turn', () => {
    // `actionError` is one shared value for the whole chat. Rendering it unconditionally would put
    // the same red text under every failed turn on screen, only one of which was pressed.
    renderBubble({ actionError: 'Failed to run that action. Please try again.' })

    expect(
      screen.queryByText('Failed to run that action. Please try again.'),
    ).not.toBeInTheDocument()
  })
})
