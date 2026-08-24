import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import { MessageBubble } from './MessageBubble'

expect.extend(toHaveNoViolations)

// specs/039-composer-interaction-states-redesign FR-020–FR-025 (User Story 5) — the new
// replay/stop control, in both of its states.
describe('MessageBubble accessibility', () => {
  it('has no automatically detectable a11y violations with no replay control (user message)', async () => {
    const { container } = render(<MessageBubble message={{ role: 'user', content: 'Hi' }} />)
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with an enabled Replay (play) control', async () => {
    const { container } = render(
      <MessageBubble
        message={{ role: 'assistant', content: 'Hello there', id: 'm1' }}
        showStopIcon={false}
        isReplayDisabled={false}
        onReplay={vi.fn()}
        onStopReplay={vi.fn()}
      />,
    )
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with a disabled Replay control', async () => {
    const { container } = render(
      <MessageBubble
        message={{ role: 'assistant', content: 'Hello there', id: 'm1' }}
        showStopIcon={false}
        isReplayDisabled={true}
        onReplay={vi.fn()}
        onStopReplay={vi.fn()}
      />,
    )
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with the Stop control showing', async () => {
    const { container } = render(
      <MessageBubble
        message={{ role: 'assistant', content: 'Hello there', id: 'm1' }}
        showStopIcon={true}
        isReplayDisabled={false}
        onReplay={vi.fn()}
        onStopReplay={vi.fn()}
      />,
    )
    expect(await axe(container)).toHaveNoViolations()
  })
})
